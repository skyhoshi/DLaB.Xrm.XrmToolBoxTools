﻿using DLaB.ModelBuilderExtensions.Entity;
using Microsoft.PowerPlatform.Dataverse.ModelBuilderLib;
using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace DLaB.ModelBuilderExtensions
{
    public class CustomizeCodeDomService : TypedServiceBase<ICustomizeCodeDomService>, ICustomizeCodeDomService
    {
        #region Entity Properties

        public bool AddDebuggerNonUserCode { get => DLaBSettings.AddDebuggerNonUserCode; set => DLaBSettings.AddDebuggerNonUserCode = value; }
        public bool AddPrimaryAttributeConsts { get => DLaBSettings.AddPrimaryAttributeConsts; set => DLaBSettings.AddPrimaryAttributeConsts = value; }
        public bool EmitEntityEtc { get => Settings.EmitEntityEtc; set => Settings.EmitEntityEtc = value; }
        public bool GenerateAnonymousTypeConstructor { get => DLaBSettings.GenerateAnonymousTypeConstructor; set => DLaBSettings.GenerateAnonymousTypeConstructor = value; }
        public bool GenerateConstructorsSansLogicalName { get => DLaBSettings.GenerateConstructorsSansLogicalName; set => DLaBSettings.GenerateConstructorsSansLogicalName = value; }
        public bool GenerateOptionSetProperties { get => DLaBSettings.GenerateOptionSetProperties; set => DLaBSettings.GenerateOptionSetProperties = value; }
        public bool GenerateTypesAsInternal { get => DLaBSettings.GenerateTypesAsInternal; set => DLaBSettings.GenerateTypesAsInternal = value; }
        public bool GenerateOptionSetMetadataAttribute { get => DLaBSettings.GenerateOptionSetMetadataAttribute; set => DLaBSettings.GenerateOptionSetMetadataAttribute = value; }
        public bool MakeAllFieldsEditable { get => DLaBSettings.MakeAllFieldsEditable; set => DLaBSettings.MakeAllFieldsEditable = value; }
        public bool UpdateMultiOptionSetAttributes { get => DLaBSettings.UpdateMultiOptionSetAttributes; set => DLaBSettings.UpdateMultiOptionSetAttributes = value; }
        public bool UpdateEnumerableEntityProperties { get => DLaBSettings.UpdateEnumerableEntityProperties; set => DLaBSettings.UpdateEnumerableEntityProperties = value; }

        #endregion Entity Properties

        public bool MakeResponseActionsEditable { get => DLaBSettings.MakeResponseMessagesEditable; set => DLaBSettings.MakeResponseMessagesEditable = value; }

        #region Sub Service Properties

        public OptionSet.CustomizeCodeDomService OptionSetCustomizer { get; set; }
        public OptionSetPropertyGenerator OptionSetPropertyCustomizer { get; set; }

        #endregion Sub Service Properties

        public CustomizeCodeDomService(ICustomizeCodeDomService defaultService, IDictionary<string, string> parameters) : base(defaultService, parameters)
        {
            Trace.TraceInformation("DLaB.ModelBuilderExtensions.CustomizeCodeDomService.CustomizeCodeDom Created!");
            Initialize();
        }

        public CustomizeCodeDomService(ICustomizeCodeDomService defaultService, DLaBModelBuilderSettings settings = null) : base(defaultService, settings)
        {
            Initialize();
        }

        private void Initialize()
        {
            OptionSetPropertyCustomizer = new OptionSetPropertyGenerator(DefaultService, Settings);
            OptionSetCustomizer = new OptionSet.CustomizeCodeDomService(DefaultService, Settings);
        }

        #region ICustomizeCodeDomService Members
        
        /// <summary>
        /// Called once for every single file.
        /// </summary>
        /// <param name="codeUnit"></param>
        /// <param name="services"></param>
        public void CustomizeCodeDom(CodeCompileUnit codeUnit, IServiceProvider services)
        {
            SetServiceCache(services);
            try
            {
                ProcessOptionSets(codeUnit, services);
                ProcessMessage(codeUnit, services);

                if (codeUnit.GetTypes()
                    .Any(t => t.IsEntityType()))
                {
                    ProcessEntity(codeUnit, services);
                    return;
                }

                ProcessServiceContext(codeUnit, services);

            }
            finally
            {
                if (GenerateTypesAsInternal)
                {
                    foreach (var type in codeUnit.GetTypes().Where(t => t.IsClass || t.IsEnum))
                    {
                        type.TypeAttributes = (type.TypeAttributes & ~TypeAttributes.VisibilityMask) | TypeAttributes.NestedAssembly;
                    }
                }

                MakeMemberNamesUnique(codeUnit);
            }

            Trace.TraceInformation("DLaB.ModelBuilderExtensions.CustomizeCodeDomService.CustomizeCodeDom Skipping processing of {0}!", string.Join(", ", codeUnit.GetTypes().Select(t => t.Name)));
        }

        private static void MakeMemberNamesUnique(CodeCompileUnit codeUnit)
        {
            foreach (var type in codeUnit.GetTypes())
            {
                foreach (var member in type.GetMembers<CodeTypeMember>())
                {
                    if (member.Name == type.Name)
                    {
                        member.Name += "__Member";
                    }
                }
            }
        }

        private void ProcessServiceContext(CodeCompileUnit codeUnit, IServiceProvider services)
        {
            if (!codeUnit.GetTypes()
                    .Any(t => t.IsContextType()))
            {
                return;
            }
            
            if (GenerateOptionSetMetadataAttribute)
            {
                new OptionSetMetadataAttributeGenerator().CustomizeCodeDom(codeUnit, services);
            }
        }

        private void ProcessEntity(CodeCompileUnit codeUnit, IServiceProvider services)
        {
            if (UpdateMultiOptionSetAttributes)
            {
                new MultiOptionSetAttributeUpdater().CustomizeCodeDom(codeUnit, services);
            }

            if (UpdateEnumerableEntityProperties)
            {
                new EnumerableEntityPropertyUpdater().CustomizeCodeDom(codeUnit, services);
            }

            if (AddPrimaryAttributeConsts)
            {
                new PrimaryAttributeGenerator(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);
            }

            if (GenerateConstructorsSansLogicalName)
            {
                new EntityConstructorsGenerator().CustomizeCodeDom(codeUnit, services);
            }

            new RelationshipConstGenerator(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);
            new AttributeConstGenerator(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);

            if (GenerateAnonymousTypeConstructor)
            {
                new AnonymousTypeConstructorGenerator(ServiceCache.EntityMetadataByLogicalName).CustomizeCodeDom(codeUnit, services);
            }

            if (GenerateOptionSetProperties)
            {
                OptionSetPropertyCustomizer.CustomizeCodeDom(codeUnit, services);
            }

            if (!EmitEntityEtc && !GenerateOptionSetProperties)
            {
                // Since Type Codes are not being generated, the enum will not be generated, but the property with the type code enum type will.  Need to revert it to int.
                new OptionSetToIntPropertyGenerator(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);
            }

            new OptionSetAttributeEnumPostFixRemover(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);

            if (MakeAllFieldsEditable)
            {
                new EditableFormattedValuesUpdater().CustomizeCodeDom(codeUnit, services);
            }

            if (AddDebuggerNonUserCode)
            {
                new Entity.MemberAttributes().CustomizeCodeDom(codeUnit, services);
            }
        }

        #endregion


        private void ProcessMessage(CodeCompileUnit codeUnit, IServiceProvider services)
        {
            FilterMessageTypes(codeUnit);
            new Message.AttributeConstGenerator(DefaultService, Settings).CustomizeCodeDom(codeUnit, services);
        }

        private void FilterMessageTypes(CodeCompileUnit codeUnit)
        {
            foreach (var type in codeUnit.GetMessageTypes())
            {
                ProcessMessage(type);
            }
        }

        private void ProcessOptionSets(CodeCompileUnit codeUnit, IServiceProvider services)
        {
            OptionSetCustomizer.CustomizeCodeDom(codeUnit, services);
        }

        private void ProcessMessage(CodeTypeDeclaration message)
        {
            var orgResponse = new CodeTypeReference(typeof(Microsoft.Xrm.Sdk.OrganizationResponse)).BaseType;
            if (MakeResponseActionsEditable && message.BaseTypes.OfType<CodeTypeReference>().Any(r => r.BaseType == orgResponse))
            {
                foreach (var prop in from CodeTypeMember member in message.Members
                         let propDom = member as CodeMemberProperty
                         where propDom != null && !propDom.HasSet
                         select propDom)
                {
                    var resultsName = prop.Name;
                    if (prop.HasGet)
                    {
                        // Theoretically this should be a call to the NamingService.GetNameForResponseField(response, field, serviceProvider), but the parameters aren't readily available, so this is easier.
                        try
                        {
                            resultsName = ((CodePrimitiveExpression)((CodeMethodInvokeExpression)((CodeConditionStatement)prop.GetStatements[0]).Condition).Parameters[0]).Value.ToString();
                        }
                        catch (Exception ex)
                        {
                            Trace.TraceError(ex.Message + Environment.NewLine + Environment.NewLine + ex);
                        }
                    }
                    var thisMember = new CodeFieldReferenceExpression(new CodeThisReferenceExpression(), "Results");
                    var indexOf = new CodeArrayIndexerExpression(thisMember, new CodePrimitiveExpression(resultsName));
                    prop.SetStatements.Add(new CodeAssignStatement(indexOf, new CodePropertySetValueReferenceExpression()));
                }
            }
        }
    }
}
