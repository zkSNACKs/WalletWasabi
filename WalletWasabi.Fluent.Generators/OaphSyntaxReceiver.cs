namespace WalletWasabi.Fluent.Generators;

internal class OaphSyntaxReceiver : FieldsWithAttributeSyntaxReceiver
{
	public override string AttributeClass => OaphGenerator.OaphAttributeDisplayString;
}