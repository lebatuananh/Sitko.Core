﻿using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace Sitko.Core.Blazor.FluentValidation
{
    /// <summary>
    /// Add Fluent Validator support to an EditContext.
    /// </summary>
    public class FluentValidator : ComponentBase, IDisposable
    {
        /// <summary>
        /// Inherited object from the FormEdit component.
        /// </summary>
        [CascadingParameter]
        private EditContext? CurrentEditContext { get; set; }

        /// <summary>
        /// Enable access to the ASP.NET Core Service Provider / DI.
        /// </summary>
        [Inject]
        private IServiceProvider ServiceProvider { get; set; } = null!;

        /// <summary>
        /// Isolate scoped DbContext to this component.
        /// </summary>
        private IServiceScope? ServiceScope { get; set; }

        /// <summary>
        /// The AbstractValidator object for the corresponding form Model object type.
        /// </summary>
        [Parameter]
        public IValidator? Validator { set; get; }

        /// <summary>
        /// The AbstractValidator objects mapping for each children / nested object validators.
        /// </summary>
        [Parameter]
        public Dictionary<Type, IValidator?> ChildValidators { set; get; } = new();

        /// <summary>
        /// Attach to parent EditForm context enabling validation.
        /// </summary>
        protected override void OnInitialized()
        {
            if (CurrentEditContext == null)
            {
                throw new InvalidOperationException($"{nameof(DataAnnotationsValidator)} requires a cascading " +
                                                    $"parameter of type {nameof(EditContext)}. For example, you can use {nameof(DataAnnotationsValidator)} " +
                                                    "inside an EditForm.");
            }

            ServiceScope = ServiceProvider.CreateScope();

            if (Validator == null)
            {
                SetFormValidator();
            }

            AddValidation();
        }

        /// <summary>
        /// Try setting the EditContext form model typed validator implementation from the DI.
        /// </summary>
        private void SetFormValidator()
        {
            var formType = CurrentEditContext!.Model.GetType();
            Validator = TryGetValidatorForObjectType(formType);
            if (Validator == null)
            {
                throw new InvalidOperationException($"FluentValidation.IValidator<{formType.FullName}> is"
                                                    + " not registered in the application service provider.");
            }
        }

        /// <summary>
        /// Try acquiring the typed validator implementation from the DI.
        /// </summary>
        /// <param name="modelType"></param>
        /// <returns></returns>
        private IValidator? TryGetValidatorForObjectType(Type modelType)
        {
            var validatorType = typeof(IValidator<>);
            var formValidatorType = validatorType.MakeGenericType(modelType);
            return ServiceScope?.ServiceProvider.GetService(formValidatorType) as IValidator;
        }

        /// <summary>
        /// Creates an instance of a ValidationContext for an object model.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="validatorSelector"></param>
        /// <returns></returns>
        private IValidationContext CreateValidationContext(object model, IValidatorSelector? validatorSelector = null)
        {
            // This method is required due to breaking changes in FluentValidation 9!
            // https://docs.fluentvalidation.net/en/latest/upgrading-to-9.html#removal-of-non-generic-validate-overload

            validatorSelector ??= ValidatorOptions.Global.ValidatorSelectors.DefaultValidatorSelectorFactory();

            // Don't need to use reflection to construct the context.
            // If you create it as a ValidationContext<object> instead of a ValidationContext<T> then FluentValidation will perform the conversion internally, assuming the types are compatible.
            var context = new ValidationContext<object>(model, new PropertyChain(), validatorSelector)
            {
                RootContextData = {["_FV_ServiceProvider"] = ServiceProvider}
            };

            // InjectValidator looks for a service provider inside the ValidationContext with this key.
            return context;
        }

        /// <summary>
        /// Add form validation logic handlers.
        /// </summary>
        private void AddValidation()
        {
            var messages = new ValidationMessageStore(CurrentEditContext!);

            // Perform object-level validation on request
            // ReSharper disable once AsyncVoidLambda
            CurrentEditContext!.OnValidationRequested +=
                async (sender, _) => await ValidateModel((EditContext)sender!, messages);

            // Perform per-field validation on each field edit
            // ReSharper disable once AsyncVoidLambda
            CurrentEditContext.OnFieldChanged += async (_, eventArgs) =>
                await ValidateField(CurrentEditContext, messages, eventArgs.FieldIdentifier);
        }

        /// <summary>
        /// Validate the whole form and trigger client UI update.
        /// </summary>
        /// <param name="editContext"></param>
        /// <param name="messages"></param>
        private async Task ValidateModel(EditContext editContext, ValidationMessageStore messages)
        {
            // <EditForm> should now be able to run async validations:
            // https://github.com/dotnet/aspnetcore/issues/11914
            var validationResults = await TryValidateModel(editContext);
            messages.Clear();

            var graph = new ModelGraphCache(editContext.Model);
            foreach (var error in validationResults.Errors)
            {
                var (propertyValue, propertyName) = graph.EvalObjectProperty(error.PropertyName);
                // while it is impossible to have a validation error for a null child property, better be safe than sorry...
                if (propertyValue != null)
                {
                    var fieldID = new FieldIdentifier(propertyValue, propertyName);
                    messages.Add(fieldID, error.ErrorMessage);
                }
            }

            editContext.NotifyValidationStateChanged();
        }

        /// <summary>
        /// Attempts to validate an entire form object model.
        /// </summary>
        /// <param name="editContext"></param>
        /// <returns></returns>
        private async Task<ValidationResult> TryValidateModel(EditContext editContext)
        {
            try
            {
                var validationContext = CreateValidationContext(editContext.Model);
                return await Validator!.ValidateAsync(validationContext);
            }
            catch (Exception ex)
            {
                var msg =
                    $"An unhandled exception occurred when validating <EditForm> model type: '{editContext.Model.GetType()}'";
                throw new UnhandledValidationException(msg, ex);
            }
        }

        /// <summary>
        /// Attempts to validate a single field or property of a form model or child object model.
        /// </summary>
        /// <param name="validator"></param>
        /// <param name="editContext"></param>
        /// <param name="fieldIdentifier"></param>
        /// <returns></returns>
        private async Task<ValidationResult> TryValidateField(IValidator validator, EditContext editContext,
            FieldIdentifier fieldIdentifier)
        {
            try
            {
                var vselector = new MemberNameValidatorSelector(new[] {fieldIdentifier.FieldName});
                var vctx = CreateValidationContext(fieldIdentifier.Model, validatorSelector: vselector);
                return await validator.ValidateAsync(vctx);
            }
            catch (Exception ex)
            {
                var msg = $"An unhandled exception occurred when validating field name: '{fieldIdentifier.FieldName}'";

                if (editContext.Model != fieldIdentifier.Model)
                {
                    msg += $" of a child object of type: {fieldIdentifier.Model.GetType()}";
                }

                msg += $" of <EditForm> model type: '{editContext.Model.GetType()}'";
                throw new UnhandledValidationException(msg, ex);
            }
        }

        /// <summary>
        /// Attempts to retrieve the field or property validator of a form model or child object model.
        /// </summary>
        /// <param name="editContext"></param>
        /// <param name="fieldIdentifier"></param>
        /// <returns></returns>
        private IValidator? TryGetFieldValidator(EditContext editContext, in FieldIdentifier fieldIdentifier)
        {
            if (fieldIdentifier.Model == editContext.Model)
            {
                return Validator;
            }

            var modelType = fieldIdentifier.Model.GetType();
            if (ChildValidators.ContainsKey(modelType))
            {
                return ChildValidators[modelType];
            }

            var validator = TryGetValidatorForObjectType(modelType);
            ChildValidators[modelType] = validator;
            return validator;
        }

        /// <summary>
        /// Validate a single field and trigger client UI update.
        /// </summary>
        /// <param name="editContext"></param>
        /// <param name="messages"></param>
        /// <param name="fieldIdentifier"></param>
        private async Task ValidateField(EditContext editContext, ValidationMessageStore messages,
            FieldIdentifier fieldIdentifier)
        {
            var fieldValidator = TryGetFieldValidator(editContext, fieldIdentifier);
            if (fieldValidator == null)
            {
                // Should not error / just fail silently for classes not supposed to be validated.
                return;
            }

            var validationResults = await TryValidateField(fieldValidator, editContext, fieldIdentifier);
            messages.Clear(fieldIdentifier);

            foreach (var error in validationResults.Errors)
            {
                messages.Add(fieldIdentifier, error.ErrorMessage);
            }

            editContext.NotifyValidationStateChanged();
        }

        #region IDisposable Support

        private bool disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    ServiceScope?.Dispose();
                }

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.

                // Set large fields to null.
                ServiceScope = null;
                Validator = null;
                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
