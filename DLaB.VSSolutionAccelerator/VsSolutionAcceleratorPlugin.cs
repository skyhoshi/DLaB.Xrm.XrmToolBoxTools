﻿using DLaB.VSSolutionAccelerator.Wizard;
using DLaB.Xrm.Entities;
using DLaB.XrmToolBoxCommon;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Source.DLaB.Xrm;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Crm.Sdk.Messages;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using Exception = System.Exception;
using NuGet;
using System.ComponentModel;

namespace DLaB.VSSolutionAccelerator
{
    public partial class VsSolutionAcceleratorPlugin : DLaBPluginControlBase, IMessageBusHost
    {
        public Settings Settings { get; set; }
        private List<Solution> _solutions = new List<Solution>();

        public VsSolutionAcceleratorPlugin()
        {
            InitializeComponent();
            if (Debugger.IsAttached)
            {
                ActionCmb.Items.Add("Generate With Default Settings");
                ActionCmb.Items.Add("Add Plugins With Default Settings");
            }

            ActionCmb.SelectedIndex = 0;
            try
            {
                UnzipTemplate();
            }
            catch (Exception ex)
            {
                throw new Exception("Unable to unzip the template.  " + ex.Message, ex);
            }
        }

        private void UnzipTemplate()
        {
            var zipPath = Path.Combine(Paths.PluginsPath, "DLaB.VSSolutionAccelerator", "Template.zip");
            var zipDirectory = Path.GetDirectoryName(zipPath) ?? "UnableToGetZipDirectory";
            if (!File.Exists(zipPath))
            {
                return;
            }

            var tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            File.SetAttributes(zipPath, FileAttributes.Normal);
            File.Move(zipPath, tmp);
            DeleteDirectory(zipDirectory);
            Directory.CreateDirectory(zipDirectory);
            ZipFile.ExtractToDirectory(tmp, zipDirectory);
            File.Delete(tmp);
        }

        private void MyPluginControl_Load(object sender, EventArgs e)
        {
            // Loads or creates the settings for the plugin
            if (!SettingsManager.Instance.TryLoad(GetType(), out Settings settings))
            {
                Settings = new Settings();

                LogWarning("Settings not found => a new settings file has been created!");
            }
            else
            {
                Settings = settings;
                LogInfo("Settings found and loaded");
            }
            Settings.Initialize();
        }

        private void VsSolutionAcceleratorPlugin_OnCloseTool(object sender, EventArgs e)
        {
            SettingsManager.Instance.Save(GetType(), Settings);
        }

        private void ShowAddAcceleratorsWizard()
        {
            using (var host = new WizardHost
            {
                Text = @"Add Accelerators Wizard",
                ShowFirstButton = false,
                ShowLastButton = false
            })
            {
                var names = GetSolutionNamesByIndex();
                foreach (var page in InitializeSolutionInfo.InitializePages(names))
                {
                    host.WizardPages.Add(page);
                }
                host.LoadWizard();
                if (host.ShowDialog() == DialogResult.OK)
                {
                    var results = host.SaveResults;
                    var info = InitializeSolutionInfo.InitializeSolution(results, GetSolutionIdsByIndex());

                    Execute(info);
                }

                host.Close();
            }
        }

        private List<KeyValuePair<int, string>> GetSolutionNamesByIndex()
        {
            return _solutions.Select((s, i) => new KeyValuePair<int, string>(i, s.FriendlyName)).ToList();
        }

        private Dictionary<int, Guid> GetSolutionIdsByIndex()
        {
            var solutionIdsByIndex = new Dictionary<int, Guid>();
            solutionIdsByIndex.AddRange(_solutions.Select((s, i) => new KeyValuePair<int, Guid>(i, s.Id)));
            return solutionIdsByIndex;
        }

        private void ShowAddAssemblyWizard()
        {
            using (var host = new WizardHost
            {
                Text = @"Add Accelerators Wizard",
                ShowFirstButton = false,
                ShowLastButton = false
            })
            {
                foreach (var page in AddProjectToSolutionInfo.InitializePages())
                {
                    host.WizardPages.Add(page);
                }
                host.LoadWizard();
                if (host.ShowDialog() == DialogResult.OK)
                {
                    var results = host.SaveResults;
                    var info = AddProjectToSolutionInfo.Create(results);
                    Execute(info);
                }

                host.Close();
            }
        }

        private void ExecuteInstallCodeSnippets()
        {
            WorkAsync(new WorkAsyncInfo("Installing Code Snippets...", (w, e) => // Work To Do Asynchronously
            {
                Logic.VisualStudio.InstallCodeSnippets(Paths.PluginsPath);
            }).WithLogger(this, TxtOutput));
        }

        private void ExecuteBttn_Click(object sender, EventArgs e)
        {
            Execute(ActionCmb.SelectedIndex);
        }

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            _solutions.Clear();
            base.UpdateConnection(newService, detail, actionName, parameter);
        }

        private void Execute(int actionIndex)
        {
            if(_solutions.Count > 0 || actionIndex == 2)
            {
                ExecuteAction(actionIndex);
                return;
            }

            Enabled = false;
            WorkAsync(new WorkAsyncInfo("Retrieving Solutions...",
                e =>
                {
                    var qe = QueryExpressionFactory.Create<Solution>(s => new { s.SolutionId, s.FriendlyName, s.Version, s.PublisherId, s.UniqueName });
                    qe.Query.Distinct = true;
                    qe.WhereEqual(
                        Solution.Fields.IsManaged, false,
                        Solution.Fields.IsVisible, true,
                        new ConditionExpression(Solution.Fields.UniqueName, ConditionOperator.NotEqual, "Default")
                    );
                    qe.AddLink<Publisher>(Solution.Fields.PublisherId, p => new {p.CustomizationPrefix });
                    qe.AddOrder(Solution.Fields.FriendlyName, OrderType.Ascending);
                    e.Result = Service.GetEntities(qe);
                }) {
                PostWorkCallBack = e =>
                {
                    _solutions = (List<Solution>)e.Result;
                    ExecuteAction(actionIndex);
                    Enabled = true;
                }
            });
        }

        private void ExecuteAction(int actionIndex)
        {
            try
            {
                switch (actionIndex)
                {
                    case 0:
                        ShowAddAcceleratorsWizard();
                        break;
                    case 1:
                        ShowAddAssemblyWizard();
                        break;
                    case 2:
                        ExecuteInstallCodeSnippets();
                        break;
                    case 3:
                        GenerateWithDefaultSettings();
                        break;
                    case 4:
                        GenerateAddAssemblyWithDefaultSettings();
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, @"Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                TxtOutput.AppendText(Environment.NewLine + ex);
            }
        }

        private void GenerateWithDefaultSettings()
        {
            var results = new AddAllWizardResults
            {
                P0AddToExistingSolution = false,
                P0SolutionPath = "C:\\Temp\\VSA\\Acme.Dataverse.sln",
                P1Namespace = "Acme.Dataverse",
                P2EarlyBound = true,
                P3SharedCommonAssemblyName = "Acme.Dataverse",
                P4SharedWorkflowProjectName = "Acme.Dataverse.WorkflowCore",
                P5UseXrmUnitTest = true, P5TestSettingsProjectName = "Acme.Dataverse.Test",
                P6CreatePluginProject = true, P6PluginProjectName = "Acme.Dataverse.Plugin", P6IncludeExamples = true,
                P7CompanyName = "Acme", P7PluginDescription = "Default Description For Plugin", P7PluginSolutionIndex = 1, P7PacAuthName = "Daryl Dev",
                P8PluginTestProjectName = "Acme.Dataverse.Plugin.Tests",
                P9CreateWorkflowProject = true, P9WorkflowProjectName = "Acme.Dataverse.Workflow", P9IncludeExamples = true,
                P10WorkflowTestProjectName = "Acme.Dataverse.Workflow.Tests",
                P11InstallCodeSnippets = true, P11IncludeCodeGen = true

            }.GetResults();

            var info = InitializeSolutionInfo.InitializeSolution(results, GetSolutionIdsByIndex());
            var solutionDir = Path.GetDirectoryName(info.SolutionPath) ?? Guid.NewGuid().ToString();
            DeleteDirectory(solutionDir);

            do
            {
                TxtOutput.AppendText("Creating Directory." + Environment.NewLine);
                Directory.CreateDirectory(solutionDir);
            } while (!Directory.Exists(solutionDir));

            Execute(info);
        }

        private static void DeleteDirectory(string directoryPath)
        {
            try
            {
                TryDeleteDirectory(directoryPath);
            }
            catch
            {
                var secondTryFailed = false;
                try
                {
                    Thread.Sleep(3000);
                    TryDeleteDirectory(directoryPath);
                }
                catch
                {
                    secondTryFailed = true;
                }

                if (secondTryFailed)
                {
                    throw;
                }
            }
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            if (Directory.Exists(directoryPath))
            {
                foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
                {
                    File.Delete(file);
                }

                Directory.Delete(directoryPath, true);
            }
        }


        private void GenerateAddAssemblyWithDefaultSettings()
        {
            if (File.Exists(@"C:\Temp\VSA\Acme.Dataverse.Lead.Plugin\Acme.Dataverse.Lead.Plugin.csproj"))
            {
                GenerateWithDefaultSettings();
                while (!Enabled)
                {
                    Thread.Sleep(10);
                }
            }
            var results = new AddPluginWorkflowWizardResults
            {
                P0SolutionPath = @"C:\Temp\VSA\Acme.Dataverse.sln",
                P1CreatePluginProject = true, P1PluginProjectName = "Acme.Dataverse.Lead.Plugin",
                P2CreatePluginXrmUnitTest = true, P2PluginTestProjectName = "Acme.Dataverse.Lead.Plugin.Tests",
                P3CreateWorkflowProject = true, P3WorkflowProjectName = "Acme.Dataverse.Lead.Workflow",
                P4CreateWorkflowXrmUnitTest = true, P4WorkflowTestProjectName ="Acme.Dataverse.Lead.Workflow.Tests",
            }.GetResults();

            var info = AddProjectToSolutionInfo.Create(results);
            Execute(info);
        }

        private void Execute(SolutionEditorInfo info)
        {
            WorkAsync(new WorkAsyncInfo("Performing requested operations...", (w, e) => // Work To Do Asynchronously
            {
                var templatePath = Path.GetFullPath(Path.Combine(Paths.PluginsPath, Settings.TemplateFolder));
                var nuGetSettings = new Logic.NuGetSettings(templatePath)
                {
                    Sources = Settings.NugetSourcesList
                };
                var arg = (object[])e.Argument;
                var solutions = (List<Solution>)arg[1];
                switch (arg[0])
                {
                    case InitializeSolutionInfo solutionInfo:
                    {
                        if (solutionInfo.InstallSnippets)
                        {
                            Logic.VisualStudio.InstallCodeSnippets(Paths.PluginsPath);
                        }
                        SetPluginPackageId(w, solutionInfo, solutions);
                        Logic.SolutionInitializer.Execute(solutionInfo, templatePath, nuGetSettings: nuGetSettings);
                        if (solutionInfo.ConfigureEarlyBound)
                        {
                            e.Result = solutionInfo.GetEarlyBoundSettingsPath();
                        }

                        break;
                    }
                    case AddProjectToSolutionInfo projectInfo:
                        SetPluginPackageId(w, projectInfo, solutions);
                        Logic.SolutionUpdater.Execute(projectInfo, templatePath, nuGetSettings: nuGetSettings);
                        break;
                }
            }).WithLogger(this, TxtOutput, new [] { (object)info, _solutions }, onComplete: e =>
            {
                if (info.CreatePlugin)
                {
                    TxtOutput.Text += string.Format("{0}{0}{1}{0}{1}{0}{0}", Environment.NewLine, "****************************************************************************************************");
                    TxtOutput.Text += $@"In order to build your plugin using the DevDeploy solution configuration to deploy your plugin to your dev environment, be sure that you've created a PAC Auth connection with the name '{info.PluginPackage.PacAuthName}'.{Environment.NewLine}";
                    TxtOutput.Text += @"Refer to this article if you need assistance with creating the named PAC Auth connection: https://nicknow.net/power-platform-pac-cli-installing-connecting-and-selecting-an-organization/" + Environment.NewLine + Environment.NewLine;
                }
                if (e.Result is string path)
                {
                    MessageBox.Show(@"The Early Bound Generator will now be opened in order to generate the early bound entities for your project.  Click the ""Generate"" button in the Early Bound Generator to generate your entities.  After generating the early bound entities, return to this tool for any additional directions!", @"Generate Early Bound Entities!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    OpenEarlyBoundGeneratorWithSettings(path);
                }
            }));
        }

        private void SetPluginPackageId(BackgroundWorker worker, SolutionEditorInfo solutionInfo, List<Solution> solutions)
        {
            if (!solutionInfo.CreatePlugin)
            {
                return;
            }
            var solution = solutions.First(s => s.Id == solutionInfo.PluginPackage.SolutionId);
            solutionInfo.PluginPackage.PackageId = GeneratePluginPackageId(worker, solution, solutionInfo.PluginName);
        }

        private string GeneratePluginPackageId(BackgroundWorker worker, Solution solution, string pluginName)
        {
            var id = Guid.Empty;
            worker.ReportProgress(0, "Creating Package...");
            var pluginPackage = InstantiatePackage(solution, pluginName);
            id = Service.Create(pluginPackage);

            worker.ReportProgress(0, "Looking up Solution Component Definition to solution...");

            // Find the right component type for the current environment
            var scd = Service.RetrieveMultiple(new QueryExpression("solutioncomponentdefinition")
            {
                ColumnSet = new ColumnSet("solutioncomponenttype"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("primaryentityname", ConditionOperator.Equal, "pluginpackage")
                    }
                }
            }).Entities.FirstOrDefault();

            if (scd == null)
            {
                throw new Exception("Unable to find the solution component type for table pluginpackage");
            }

            worker.ReportProgress(0, "Adding Package to Solution...");

            Service.Execute(new AddSolutionComponentRequest
            {
                AddRequiredComponents = false,
                ComponentId = id,
                ComponentType = scd.GetAttributeValue<int>("solutioncomponenttype"),
                SolutionUniqueName = solution.UniqueName
            });

            return id.ToString();
        }

        private Entity InstantiatePackage(Solution solution, string pluginName)
        {
            var packagePath = Path.Combine(Paths.PluginsPath, Settings.TemplateFolder, "DefaultPluginPackage.nupkg");
            if (!File.Exists(packagePath))
            {
                MessageBox.Show(this, $@"File not found : {packagePath}", @"Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var package = new Entity("pluginpackage")
            {
                ["solutionid"] = solution.Id
            };
            using (var p = Package.Open(packagePath, FileMode.Open))
            {
                foreach (var part in p.GetParts())
                {
                    if (!part.Uri.ToString().EndsWith(".nuspec"))
                    {
                        continue;
                    }

                    using (var stream = part.GetStream())
                    {
                        var xReader = new XmlTextReader(stream);
                        var doc = new XmlDocument();
                        doc.Load(xReader);

                        XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                        nsmgr.AddNamespace("ns", doc.DocumentElement.NamespaceURI);

                        var metadata = doc.SelectSingleNode("ns:package/ns:metadata", nsmgr);

                        if (metadata == null)
                        {
                            MessageBox.Show(this, "Package metadata not found\r\n\r\nCould not find the package/metadata node in " + part.Uri, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return null;
                        }

                        var version = metadata.SelectSingleNode("ns:version", nsmgr)?.InnerText;
                        if (version == null)
                        {
                            MessageBox.Show(this, "Package metadata not found\r\n\r\nCould not find the package/metadata/version node in " + part.Uri, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return null;
                        }

                        var name = solution.GetAliasedEntity<Publisher>().CustomizationPrefix + "_" + pluginName;
                        package["name"] = name;
                        package["version"] = version;
                        package["uniquename"] = name;
                    }
                }
            }

            using (var reader = new FileStream(packagePath, FileMode.Open))
            {
                using (var ms = new MemoryStream())
                {
                    reader.CopyTo(ms);

                    package["content"] = Convert.ToBase64String(ms.ToArray());
                }
            }

            return package;
        }

        private void ActionCmb_SelectedIndexChanged(object sender, EventArgs e)
        {
            ExecuteBttn.Enabled = ActionCmb.SelectedIndex >= 0;
        }

        public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;

        public void OnIncomingMessage(MessageBusEventArgs message)
        {
            if (message.SourcePlugin != "Visual Studio Solution Accelerator")
            {
                return;
            }
            throw new NotImplementedException();
        }

        private void OpenEarlyBoundGeneratorWithSettings(string path)
        {
            if (OnOutgoingMessage is null)
            {
                var message = @"No events are registered on the OnOutgoingMessage event!  Unable to open the Early Bound Generator!";
                MessageBox.Show(message, @"Error!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                TxtOutput.AppendText(Environment.NewLine + message);
                return;
            }

            var request = new Dictionary<string, object>{
                { "path", path }
            };
            OnOutgoingMessage(this, new MessageBusEventArgs("Early Bound Generator V2")
            {
                TargetArgument = request
            });
        }
    }
}