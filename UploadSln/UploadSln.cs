using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace UploadSln
{
	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class UploadSln
	{
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("ef30056f-4583-4ced-9627-c50cd5bf6ce2");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		/// Initializes a new instance of the <see cref="UploadSln"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private UploadSln( AsyncPackage package, OleMenuCommandService commandService, DTE2 dte )
		{
			this.dte = dte ?? throw new ArgumentNullException( nameof( dte ) );
			this.package = package ?? throw new ArgumentNullException( nameof( package ) );
			commandService = commandService ?? throw new ArgumentNullException( nameof( commandService ) );

			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(this.Execute, menuCommandID);
			commandService.AddCommand( menuItem );
		}

		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static UploadSln Instance
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the service provider from the owner package.
		/// </summary>
		private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
		{
			get
			{
				return this.package;
			}
		}
		/// <summary>
		/// DTE Object
		/// </summary>
		private DTE2 dte;

		/// <summary>
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync( AsyncPackage package )
		{
			// Switch to the main thread - the call to AddCommand in UploadSln's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync( package.DisposalToken );

			OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
			var dte = await package.GetServiceAsync( typeof(DTE)) as DTE2;
			Instance = new UploadSln( package, commandService, dte );
		}

		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void Execute( object sender, EventArgs e )
		{
			//	コマンドハンドラなので、メインのUIスレッドから呼ばれてるはずだけど、非同期でもいいのでとりあえず考慮しない方向で検討
			ThreadHelper.ThrowIfNotOnUIThread();
			if( dte == null )
			{
				return;
			}
			OutputWindowPane pane = null;
			var paneName = "ソリューションファイルのアップロード";
			foreach( var obj in dte.ToolWindows.OutputWindow.OutputWindowPanes )
			{
				pane = obj as OutputWindowPane;
				if( pane.Name == paneName )
				{
					break;
				}
				pane = null;
			}
			if( pane == null )
			{
				pane = dte.ToolWindows.OutputWindow.OutputWindowPanes.Add( paneName );
			}
			//	中身を空にして、出力準備を行う
			pane.Clear();
			pane.Activate();
			dte.ToolWindows.OutputWindow.Parent.Activate();
			//	同期処理でまずは、アイテム列挙
			var uploadSln = ListupTargetFiles( dte, pane );
			//	ここは行った先で非同期になる
			UploadSolution( uploadSln );
		}

		private UploadSolution ListupTargetFiles( DTE2 dte, OutputWindowPane pane )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			var currCursor = Cursor.Current;
			Cursor.Current = Cursors.WaitCursor;
			//	ソリューションアイテムへのアクセスはCOMベースになるため、UIスレッドで行うことにした(同期じゃないと面倒なため)
			var solution = dte.Solution;
			var solutionPath = solution.FullName;
			if( solution == null || solution.Count == 0 || string.IsNullOrWhiteSpace( solutionPath ) || !File.Exists( solutionPath ) ||
				solution.Projects.Kind != EnvDTE.Constants.vsProjectsKindSolution )
			{
				pane.WriteLine( $"ソリューションを開いていません。dte.Solution.FullName=\"{solutionPath}\"" );
				return null;
			}
			pane.WriteLine( "すべてのファイルを保存しています。" );
			//	すべて保存のコマンドを発行することで保存を行う
			dte.ExecuteCommand( "File.SaveAll", "" );

			//	ソリューションファイルを含むすべてのコピー対象アイテムをリストアップ
			var uploadSln = new UploadSolution( dte, pane );
			uploadSln.ListupUploadFiles();
			Cursor.Current = currCursor;
			return uploadSln;
		}

		private void UploadSolution( UploadSolution uploadSln )
		{
			if( uploadSln.IsExecute )
			{
				//	非同期に処理するけど待たない
				var task = Task.Run( uploadSln.UploadAllFilesAsync );
			}
		}
	}
}
