//
// Gendarme.Framework.Rocks.FieldRocks
//
// Authors:
//	Sebastien Pouliot  <sebastien@ximian.com>
//	Andreas Noever <andreas.noever@gmail.com>
//
// Copyright (C) 2008, 2010 Novell, Inc (http://www.novell.com)
// (C) 2008 Andreas Noever
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using Mono.Cecil;

namespace Gendarme.Framework.Rocks {

	public static class FieldRocks {
		/// <summary>
		/// Check if the field is visible outside of the assembly.
		/// </summary>
		/// <param name="self">The FieldReference on which the extension method can be called.</param>
		/// <returns>True if the field can be used from outside of the assembly, false otherwise.</returns>
		public static bool IsVisible (this FieldReference self)
		{
			if (self == null)
				return false;

			FieldDefinition field = self.Resolve ();
			if ((field == null) || field.IsPrivate || field.IsAssembly)
				return false;

			return field.DeclaringType.Resolve ().IsVisible ();
		}
	}
}