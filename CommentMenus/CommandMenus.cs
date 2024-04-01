using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using Task = System.Threading.Tasks.Task;

namespace CommentMenus
{
	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class CommandMenus
	{
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int cmdidInsAdj = 0x0200;
		public const int cmdidInsFix = 0x0201;
		public const int cmdidInsDate = 0x0202;
		public const int cmdidInsGuid = 0x0203;
		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("54f2dde3-c5d4-4168-a390-22147f5ef411");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		/// EnvDTE.DTE Instance
		/// </summary>
		private readonly DTE DTE;

		private readonly string OwnerName;

		/// <summary>
		/// Initializes a new instance of the <see cref="CommandMenus"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private CommandMenus( AsyncPackage package, OleMenuCommandService commandService, DTE dte )
		{
			this.package = package ?? throw new ArgumentNullException( nameof( package ) );
			commandService = commandService ?? throw new ArgumentNullException( nameof( commandService ) );
			DTE = dte ?? throw new ArgumentNullException( nameof( dte ) );

			var rootKey = RegistryKey.OpenBaseKey( RegistryHive.LocalMachine, (Environment.Is64BitOperatingSystem) ? RegistryView.Registry64 : RegistryView.Default );
			RegistryKey keyOsInfo = rootKey.OpenSubKey( @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false );
			string userName = keyOsInfo.GetValue( "RegisteredOwner" ) as string;

			var pos = (string.IsNullOrWhiteSpace( userName )) ? -1 : userName.IndexOfAny( new char[] { ' ', '　' } );
			if( pos == -1 )
			{
				userName = Environment.UserName;
			}
			else
			{
				userName = userName.Substring( 0, pos );
			}
			OwnerName = userName;

			commandService.AddCommand( new MenuCommand( this.ExecuteInsAdj, new CommandID( CommandSet, cmdidInsAdj ) ) );
			commandService.AddCommand( new MenuCommand( this.ExecuteInsFix, new CommandID( CommandSet, cmdidInsFix ) ) );
			commandService.AddCommand( new MenuCommand( this.ExecuteInsDate, new CommandID( CommandSet, cmdidInsDate ) ) );
			commandService.AddCommand( new MenuCommand( this.ExecuteInsGuid, new CommandID( CommandSet, cmdidInsGuid ) ) );
		}
		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static CommandMenus Instance
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
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync( AsyncPackage package )
		{
			// Switch to the main thread - the call to AddCommand in CommandMenus's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync( package.DisposalToken );

			OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
			var dte = await package.GetServiceAsync( typeof(DTE) ) as DTE;

			Instance = new CommandMenus( package, commandService, dte );
		}

		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void ExecuteInsAdj( object sender, EventArgs e )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			InsertComment( "Adj!! ", "Adjコメントの挿入" );
		}
		private void ExecuteInsFix( object sender, EventArgs e )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			InsertComment( "Fix!! ", "Adjコメントの挿入" );
		}
		private void ExecuteInsDate( object sender, EventArgs e )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			InsertComment( "", "日付の挿入" );
		}
		private void ExecuteInsGuid( object sender, EventArgs e )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			InsertGuid( Guid.NewGuid(), "Guidの挿入" );
			// GUIDの挿入はコメント挿入とは異なる
		}
		private void InsertComment( string prefix, string undoName )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if( DTE == null || DTE.ActiveDocument == null )
			{
				return;
			}
			var type = GetCommentType( DTE.ActiveDocument );
			var sep = LineComSeparator.Tab;
			if( IsBlockCommentType( type ) )
			{
				sep = LineComSeparator.None;
			}
			string lineText = LineComPrefix( type, sep );
			lineText += prefix;
			lineText += DateTime.Today.ToShortDateString(); //	OS設定の短い時間をセットアップする
			lineText += ' ';
			lineText += OwnerName;
			lineText += LineComPostFix( type, sep );

			InsertText( undoName, lineText );
		}
		private void InsertGuid( Guid guid, string undoName )
		{
			// 取り込み形式を設定できるといいのかな？でも固定で括弧なしでよい
			InsertText( undoName, guid.ToString() ); // 括弧の有無はどっちでもよい(最終的にはついてるほうに寄せていいと思うけど)
		}

		private void InsertText( string undoName, string insText )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if( DTE == null || DTE.ActiveDocument == null )
			{
				return;
			}
			using( var undo = new VsUndo( DTE.UndoContext, undoName ) )
			{
				var selText = DTE.ActiveDocument.Selection as EnvDTE.TextSelection;
				selText.Text = insText;
			}
		}
		private enum LineComSeparator
		{
			None,
			Space = ' ',
			Tab = '\t',
		}
		private static string LineComPrefix( CommentType type, LineComSeparator sep = LineComSeparator.None )
		{
			string lineText = "";
			switch( type )
			{
				case CommentType.TypeCs:
					lineText = "//";
					break;
				case CommentType.TypeVb:
					lineText = "'";
					break;
				case CommentType.TypeXml:
					lineText = "<!--";
					break;
			}
			if( string.IsNullOrWhiteSpace( lineText ) == false && sep != LineComSeparator.None )
			{
				lineText += (char)sep;
			}
			return lineText;
		}
		private static string LineComPostFix( CommentType type, LineComSeparator sep = LineComSeparator.None )
		{
			if( type != CommentType.TypeXml )
			{
				return "";  //	ブロックコメント型はXMLのみセットする
			}
			string lineText = "";
			if( sep != LineComSeparator.None )
			{
				lineText += (char)sep;
			}
			lineText += "-->";
			return lineText;
		}
		enum CommentType
		{
			Unknown,
			TypeCs,
			TypeVb,
			TypeXml,
		}
		private CommentType GetCommentType( Document doc )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if( doc == null )
			{
				return CommentType.Unknown;
			}
			var prjItem = doc.ProjectItem;
			if( prjItem == null )
			{
				return CommentType.Unknown;
			}
			if( prjItem.FileCodeModel != null )
			{
				EnvDTE.FileCodeModel fileCodeModel = prjItem.FileCodeModel;
				switch( fileCodeModel.Language )
				{
					case EnvDTE.CodeModelLanguageConstants.vsCMLanguageCSharp:
					case EnvDTE.CodeModelLanguageConstants.vsCMLanguageMC:
					case EnvDTE.CodeModelLanguageConstants.vsCMLanguageVC:
					case EnvDTE.CodeModelLanguageConstants.vsCMLanguageIDL:
						return CommentType.TypeCs;
					case EnvDTE.CodeModelLanguageConstants.vsCMLanguageVB:
						return CommentType.TypeVb;
				}
			}
			if( doc.Language == "XML" )
			{
				return CommentType.TypeXml;
			}
			//	認識できない形式 == プレーンテキスト扱い
			Trace.WriteLine( $"NonDetected Item. Name={prjItem.Name}, Kind={prjItem.Kind}, Language={doc.Language}" );
			return CommentType.Unknown;
		}
		private static bool IsBlockCommentType( CommentType type )
		{
			return type == CommentType.TypeXml;
		}
	}
}
