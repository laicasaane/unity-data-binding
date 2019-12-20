using Realmar.DataBindings.Editor.Extensions;
using System;
using System.Reflection;
using System.Text;

namespace Realmar.DataBindings.Editor.TestFramework
{
	internal class Binding : MarshalByRefObject, IBinding
	{
		internal class Arguments
		{
			internal BindingAttribute BindingAttribute { get; set; }
			internal MemberInfo SourceProperty { get; set; }
			internal MemberInfo TargetProperty { get; set; }
			internal object Source { get; set; }
			internal object Target { get; set; }
		}

		private Arguments _arguments;

		public BindingAttribute BindingAttribute => _arguments.BindingAttribute;


		public Binding(Arguments arguments)
		{
			_arguments = arguments;
		}

		public void SetSourceProperty(object value)
		{
			_arguments.SourceProperty.SetFieldOrPropertyValue(_arguments.Source, value);
		}

		public void SetTargetProperty(object value)
		{
			_arguments.TargetProperty.SetFieldOrPropertyValue(_arguments.Target, value);
		}

		public object GetSourceProperty()
		{
			return _arguments.SourceProperty.GetFieldOrPropertyValue(_arguments.Source);
		}

		public object GetTargetProperty()
		{
			return _arguments.TargetProperty.GetFieldOrPropertyValue(_arguments.Target);
		}

		public override string ToString()
		{
			var sb = new StringBuilder();

			sb.Append(
				$"{_arguments.SourceProperty.DeclaringType.FullName}::{_arguments.SourceProperty.Name} ({_arguments.SourceProperty.GetHashCode()}) ");
			sb.Append(
				$"--> {_arguments.TargetProperty.DeclaringType.FullName}::{_arguments.TargetProperty.Name} ({_arguments.TargetProperty.GetHashCode()})");
			sb.AppendLine("ID = {_arguments.BindingAttribute.TargetId}");
			sb.AppendLine($"Type = {_arguments.BindingAttribute.BindingType}");
			sb.AppendLine($"Source = {_arguments.Source.GetHashCode()}");
			sb.AppendLine($"Target = {_arguments.Target.GetHashCode()}");

			return sb.ToString();
		}
	}
}
