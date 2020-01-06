using JetBrains.Annotations;
using System;
using System.Reflection;

namespace Realmar.DataBindings.Editor.TestFramework.Sandbox
{
	internal class Binding : MarshalByRefObject, IBinding
	{
		internal class Arguments
		{
			[NotNull] internal BindingAttribute BindingAttribute { get; set; }
			[NotNull] internal MemberInfo SourceProperty { get; set; }
			[CanBeNull] internal MemberInfo TargetProperty { get; set; }
			[NotNull] internal object Source { get; set; }
			[CanBeNull] internal object Target { get; set; }
		}

		private Arguments _arguments;

		[CanBeNull] public IUUTBindingObject Target { get; }
		[NotNull] public IUUTBindingObject Source { get; }
		[NotNull] public BindingAttribute BindingAttribute => _arguments.BindingAttribute;


		internal Binding(Arguments arguments)
		{
			_arguments = arguments;
			Target = new UUTBindingObject(_arguments.TargetProperty, _arguments.Target);
			Source = new UUTBindingObject(_arguments.SourceProperty, _arguments.Source);
		}
	}
}
