namespace Presentation.WebApi.Attributes;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NoAmbientTransactionAttribute : Attribute;
