//
// RazorTemplateFileGenerator.cs
//
// Author:
//       Michael Hutchinson <mhutch@xamarin.com>
//
// Copyright (c) 2012 Xamarin Inc (http://xamarin.com)
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

using System;
using MonoDevelop.Ide.CustomTools;
using System.CodeDom.Compiler;
using MonoDevelop.Projects;
using System.IO;
using Mono.TextTemplating;
using MonoDevelop.Core;
using System.Threading;
using RazorGenerator.Core;
using MonoDevelop.TextTemplating;
using MonoDevelop.Ide;
using MonoDevelop.Projects.Text;
using System.Collections.Generic;

namespace MonoDevelop.RazorGenerator
{
	class RazorTemplatePreprocessor : ISingleFileCustomTool
	{
		public static RazorHost CreateHost (string fullPath, string projectRelativePath)
		{
			var directives = new Dictionary<string, string> ();
			var properties = new List<string[]> ();
			var codeTransformer = new PreprocessedTemplateCodeTransformer (directives, properties);
			var codeDomProvider = new Microsoft.CSharp.CSharpCodeProvider ();
			var host = new RazorHost (projectRelativePath, fullPath, codeTransformer, codeDomProvider, directives);
			host.Parser = new PreprocessedCSharpRazorCodeParser (directives, properties);
			return host;
		}

		//from TextTemplatingFilePreprocessor
		static string GetNamespaceHint (ProjectFile file, string outputFile)
		{
			string ns = file.CustomToolNamespace;
			if (string.IsNullOrEmpty (ns) && !string.IsNullOrEmpty (outputFile)) {
				var dnp = ((DotNetProject) file.Project);
				ns = dnp.GetDefaultNamespace (outputFile);
			}
			return ns;
		}

		public IAsyncOperation Generate (IProgressMonitor monitor, ProjectFile file, SingleFileCustomToolResult result)
		{
			return new ThreadAsyncOperation (delegate {
				try {
					GenerateInternal (monitor, file, result);
				} catch (Exception ex) {
					result.UnhandledException = ex;
				}
			}, result);
		}

		void GenerateInternal (IProgressMonitor monitor, ProjectFile file, SingleFileCustomToolResult result)
		{
			var dnp = file.Project as DotNetProject;
			if (dnp == null || dnp.LanguageName != "C#") {
				var msg = "Razor templates are only supported in C# projects";
				result.Errors.Add (new CompilerError (file.Name, -1, -1, null, msg));
				monitor.Log.WriteLine (msg);
				return;
			}

			var host = CreateHost (file.FilePath, file.ProjectVirtualPath);
			host.EnableLinePragmas = true;
			host.Error += (s, e) =>  {
				Console.WriteLine (e.ErrorMessage);
				result.Errors.Add (new CompilerError (file.FilePath, e.LineNumber, e.ColumnNumber, e.ErrorCode.ToString (), e.ErrorMessage));
			};

			var defaultOutputName = file.FilePath.ChangeExtension (".cs");

			var ns = GetNamespaceHint (file, defaultOutputName);
			host.DefaultNamespace = ns;

			var code = host.GenerateCode ();

			var writer = new MonoDevelop.DesignerSupport.CodeBehindWriter ();
			writer.WriteFile (defaultOutputName, code);
			writer.WriteOpenFiles ();

			result.GeneratedFilePath = defaultOutputName;

			foreach (var err in result.Errors) {
				monitor.Log.WriteLine (err.ToString ());
			}
		}
	}
}