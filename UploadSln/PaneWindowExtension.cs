using EnvDTE;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace UploadSln
{
	internal static class PaneWindowExtension
	{
		public static void WriteLine( this OutputWindowPane pane, string value )
		{
			Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
			Debug.WriteLine( value );
			pane.OutputString( value + "\n" );
		}
		public static void WriteLine( this OutputWindowPane pane )
		{
			Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
			Debug.WriteLine( "" );
			pane.OutputString( "\n" );
		}
		public static async Task WriteLineAsync( this OutputWindowPane pane, string value )
		{
			await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
			Debug.WriteLine( value );
			pane.OutputString( value + "\n" );
		}
		public static async Task WriteLineAsync( this OutputWindowPane pane )
		{
			await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
			Debug.WriteLine( "" );
			pane.OutputString( "\n" );
		}
	}
}
