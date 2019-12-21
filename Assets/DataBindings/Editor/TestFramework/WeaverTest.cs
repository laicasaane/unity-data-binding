using NUnit.Framework;
using Realmar.DataBindings.Editor.Exceptions;
using System;
using System.Runtime.CompilerServices;
using static Realmar.DataBindings.Editor.YeetHelpers;
using Assert = NUnit.Framework.Assert;

namespace Realmar.DataBindings.Editor.TestFramework
{
	internal class WeaverTest
	{
		private readonly WeaverTestFacade _weaverTestFacade = new WeaverTestFacade();

		[OneTimeSetUp]
		public virtual void SetupFixture()
		{
		}

		[OneTimeTearDown]
		public virtual void TeardownFixture()
		{
			_weaverTestFacade?.Dispose();
		}

		protected void AssertMissingSymbolExceptionThrown<TException>(
			string fullSymbolName,
			[CallerMemberName] string testName = null)
			where TException : MissingSymbolException
		{
			AssertMissingSymbolExceptionThrown<TException>(fullSymbolName, null, testName);
		}

		protected void AssertMissingSymbolExceptionThrown<TException>(
			string fullSymbolName,
			Action<TException> customAssertions,
			[CallerMemberName] string testName = null)
			where TException : MissingSymbolException
		{
			YeetIfNull(testName, nameof(testName));

			var exception = Assert.Throws<TException>(
				() => _weaverTestFacade.CompileAndWeave(GetType(), testName));
			Assert.That(exception.SymbolName, Is.EqualTo(fullSymbolName));
			customAssertions?.Invoke(exception);
		}
	}
}