using EnvDTE;
using EnvDTE80;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace UploadSln
{
	internal class UploadSolution
	{
		public const string wixProjectItemKindProjectFile = "{930c7802-8a8c-48f9-8165-68863bccd9dd}";
		public const string vcContextGuidVCProject = "{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}";
		private DTE2 DTE { get; set; }
		private OutputWindowPane Pane { get; set; }
		private HashSet<string> TargetFiles { get; set; }
		private HashSet<string> UploadedFiles { get; set; }
		private string BaseDir { get; set; }
		private string SolutionName { get; set; }
		private string SolutionPath { get; set; }
		private UploadInfo UploadInfo { get; set; }
		public bool IsExecute { get; internal set; }

		public UploadSolution( DTE2 dte, OutputWindowPane pane )
		{
			//	同期処理
			ThreadHelper.ThrowIfNotOnUIThread();
			DTE = dte;
			Pane = pane;
			SolutionPath = dte.Solution.FullName;
			SolutionName = dte.Solution.Properties.Item( "Name" ).Value.ToString();
			BaseDir = Path.GetDirectoryName( SolutionPath ).ToLower();
			TargetFiles = new HashSet<string>();
			UploadedFiles = new HashSet<string>();
			IsExecute = false;
		}
		/// <summary>
		/// COMアクセスの必要なファイルの列挙を同期処理で行う
		/// </summary>
		internal void ListupUploadFiles()
		{
			//	同期処理
			ThreadHelper.ThrowIfNotOnUIThread();
			UploadInfo = LoadXML();
			if( UploadInfo == null )
			{
				return;
			}
			//	ソリューションファイルのパスを保存
			Pane.WriteLine( SolutionName + " のリストアップ中..." );
			AddUploadFile( SolutionPath );
			//	ソリューションファイル配下のファイルを一通りコピーする
			foreach( Project prj in DTE.Solution.Projects )
			{
				AddUploadFile( prj );
			}
			//	一つ以上登録されていたら実行する
			IsExecute = TargetFiles.Count != 0;
		}
		/// <summary>
		/// AddUploadFile アップロードするファイルを追加(Project, ProjectItem, string)
		/// </summary>
		/// <param name="prj"></param>
		private void AddUploadFile( Project prj )
		{
			//	同期処理
			ThreadHelper.ThrowIfNotOnUIThread();
			if( prj.Kind == EnvDTE.Constants.vsProjectKindMisc )
			{
				Pane.WriteLine( $"Skip Projects...{prj.Name}/Kind=\"{ConstantsNameFromGUID( prj.Kind )}\"" );
				return;
			}
			Pane.WriteLine( $"Add...{prj.Name}" );
			var filePath = prj.FullName;
			if( !string.IsNullOrWhiteSpace( filePath ) )
			{
				AddUploadFile( filePath );
			}
			//	プロジェクトにぶら下がるプロジェクトアイテムのアップロード
			foreach( ProjectItem prjItem in prj.ProjectItems )
			{
				AddUploadFile( prjItem );
			}
		}
		private void AddUploadFile( ProjectItem prjItem )
		{
			//	同期処理
			ThreadHelper.ThrowIfNotOnUIThread();
			var fileCount = prjItem.FileCount;
			if( fileCount > 0 )
			{
				for( short index = 1 ; index <= fileCount ; index++ )
				{
					var pathName = prjItem.FileNames[index];
					if( !string.IsNullOrWhiteSpace( pathName ) )
					{
						//	projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFolder 
						//	projectItem.Kind == EnvDTE.Constants.vsProjectItemKindSolutionItems:
						//	projectItem.Kind == EnvDTE.Constants.vsProjectItemKindPhysicalFile:
						AddUploadFile( pathName );
					}
				}
			}
			if( prjItem.SubProject != null )
			{
				AddUploadFile( prjItem.SubProject );
			}
			else if( prjItem.ProjectItems != null )
			{
				foreach( ProjectItem subItem in prjItem.ProjectItems )
				{
					AddUploadFile( subItem );
				}
			}
		}
		private void AddUploadFile( string srcPath )
		{
			//	除外ファイルはリストに載せない
			if( UploadInfo.IsExclude( srcPath ) )
			{
				return;
			}
			//	ベースフォルダのサブフォルダ以外はコピー対象に含めない
			if( !srcPath.ToLower().Contains( BaseDir ) )
			{
				return;
			}
			Debug.Assert( srcPath.Length > BaseDir.Length );
			TargetFiles.Add( srcPath );	//	フルパスのままセットする(物理パス参照したいことが多いため)
		}
		/// <summary>
		///	アップロード情報XMLのロード
		/// </summary>
		/// <returns></returns>
		private UploadInfo LoadXML()
		{
			//	同期処理
			ThreadHelper.ThrowIfNotOnUIThread();
			//	情報ファイルはソリューション名.UploadInfo.xml のみに変更
			var filePath = Path.ChangeExtension( SolutionPath, "UploadInfo.xml" );
			if( !File.Exists( filePath ) )
			{
				Pane.WriteLine( "アップロードファイルがありません。アップロードを中止します。" );
				Pane.WriteLine( $"ファイル \"{filePath}\"" );
				return null;
			}
			using( var stream = new StreamReader( filePath ) )
			{
				var serializer = new XmlSerializer( typeof( UploadInfo ) );
				return serializer.Deserialize( stream ) as UploadInfo;
			}
		}
		/// <summary>
		/// 非同期にファイルをアップロード
		/// </summary>
		public async Task UploadAllFilesAsync()
		{
			await Pane.WriteLineAsync( SolutionName + " のアップロード中..." );
			await Pane.WriteLineAsync( $"Upload to...\"{UploadInfo.Target.Folder}\"" );
#if EXEC_UPLOAD
			await CreateDirectoryAsync( UploadInfo.Target.Folder );
#endif
			//	ソースパスを全部リストアップ済みなので、あとはひたすら送り出すだけ
			foreach( var srcPath in TargetFiles )
			{
				await UploadFileAsync( srcPath );
			}
			await Pane.WriteLineAsync( SolutionName + " のアップロード終了" );
		}
		/// <summary>
		/// ファイルのアップロード
		/// </summary>
		/// <param name="srcPath">アップロード対象ファイル</param>
		private async Task UploadFileAsync( string srcPath )
		{
			string relPath = srcPath.Substring( BaseDir.Length + 1 );
			string dstPath = Path.Combine( UploadInfo.Target.Folder, relPath );
			//	ディレクトリの場合
			if( Directory.Exists( srcPath ) )
			{
				await CreateDirectoryAsync( dstPath );
				return;
			}
			//	アップロード一覧はHashSetにしたので常にユニーク
			UploadedFiles.Add( relPath );
			bool doCopy = true;
			bool existFile = File.Exists( dstPath );
			//	既存ファイルはタイムスタンプをコピー条件にして不用意に古いものがアップされないようにする
			if( existFile )
			{
				var result = await CompareFileTimeAsync( srcPath, dstPath );
				doCopy = result > 0;
			}
			if( doCopy )
			{
				var dstDir = Path.GetDirectoryName( dstPath );
				if( !Directory.Exists( dstDir ) )
				{
					await CreateDirectoryAsync( dstDir );
				}
				var flagStr = (existFile) ? "Copy:" : "New:";
				await Pane.WriteLineAsync( $"{flagStr} {relPath} -> {dstPath}" );
#if EXEC_UPLOAD
				await Task.Run( () =>
				{
					//	ファイルをコピーする。もし、リードオンリー属性がついていたら強制で排除する(VSS対応時の名残)
					File.Copy( srcPath, dstPath, true );
					FileAttributes fileAttr = File.GetAttributes( srcPath );
					fileAttr &= ~FileAttributes.ReadOnly;
					File.SetAttributes( dstPath, fileAttr );
				} );
#endif
			}
		}
		private async Task CreateDirectoryAsync( string dstPath )
		{
			if( !Directory.Exists( dstPath ) )
			{
				await Pane.WriteLineAsync( $"CreateDirectory...{dstPath}" );
#if EXEC_UPLOAD
				await Task.Run( () => Directory.CreateDirectory( dstPath ) );
#endif
			}
		}
		static private async Task<int> CompareFileTimeAsync( string srcFilePath, string dstFilePath )
		{
			FileInfo srcFileInfo = await Task.Run( () => new FileInfo( srcFilePath ) );
			FileInfo dstFileInfo = await Task.Run( () => new FileInfo( dstFilePath ) );
			return DateTime.Compare( srcFileInfo.LastWriteTimeUtc, dstFileInfo.LastWriteTimeUtc );
		}
		private static string ConstantsNameFromGUID( string guidStr )
		{
			string resultStr = "";
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsDocumentKindText, "vsDocumentKindText" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindSubProject, "vsProjectItemKindSubProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindVirtualFolder, "vsProjectItemKindVirtualFolder" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, "vsProjectItemKindPhysicalFolder" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindPhysicalFile, "vsProjectItemKindPhysicalFile" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextMacroRecordingToolbar, "vsContextMacroRecordingToolbar" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextMacroRecording, "vsContextMacroRecording" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextSolutionHasMultipleProjects, "vsContextSolutionHasMultipleProjects" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextSolutionHasSingleProject, "vsContextSolutionHasSingleProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextEmptySolution, "vsContextEmptySolution" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextNoSolution, "vsContextNoSolution" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextDesignMode, "vsContextDesignMode" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextFullScreenMode, "vsContextFullScreenMode" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDMiscFilesProjectItem, "vsCATIDMiscFilesProjectItem" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDMiscFilesProject, "vsCATIDMiscFilesProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDSolutionBrowseObject, "vsCATIDSolutionBrowseObject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDSolution, "vsCATIDSolution" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDGenericProject, "vsCATIDGenericProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextDebugging, "vsContextDebugging" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsAddInCmdGroup, "vsAddInCmdGroup" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindMacroExplorer, "vsWindowKindMacroExplorer" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindObjectBrowser, "vsWindowKindObjectBrowser" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindOutput, "vsWindowKindOutput" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindSolutionExplorer, "vsWindowKindSolutionExplorer" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindProperties, "vsWindowKindProperties" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindWatch, "vsWindowKindWatch" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindAutoLocals, "vsWindowKindAutoLocals" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindLocals, "vsWindowKindLocals" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindThread, "vsWindowKindThread" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindCallStack, "vsWindowKindCallStack" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindToolbox, "vsWindowKindToolbox" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindTaskList, "vsWindowKindTaskList" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindTextView, "vsViewKindTextView" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindDesigner, "vsViewKindDesigner" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindCode, "vsViewKindCode" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindDebugging, "vsViewKindDebugging" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindAny, "vsViewKindAny" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsViewKindPrimary, "vsViewKindPrimary" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsDocumentKindBinary, "vsDocumentKindBinary" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsDocumentKindResource, "vsDocumentKindResource" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsDocumentKindHTML, "vsDocumentKindHTML" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindDynamicHelp, "vsWindowKindDynamicHelp" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsContextSolutionBuilding, "vsContextSolutionBuilding" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindClassView, "vsWindowKindClassView" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindDocumentOutline, "vsWindowKindDocumentOutline" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectsKindSolution, "vsProjectsKindSolution" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindSolutionItems, "vsProjectItemKindSolutionItems" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemsKindSolutionItems, "vsProjectItemsKindSolutionItems" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectKindSolutionItems, "vsProjectKindSolutionItems" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectKindUnmodeled, "vsProjectKindUnmodeled" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemKindMisc, "vsProjectItemKindMisc" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectItemsKindMisc, "vsProjectItemsKindMisc" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsProjectKindMisc, "vsProjectKindMisc" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWizardNewProject, "vsWizardNewProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWizardAddItem, "vsWizardAddItem" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWizardAddSubProject, "vsWizardAddSubProject" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindWebBrowser, "vsWindowKindWebBrowser" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindLinkedWindowFrame, "vsWindowKindLinkedWindowFrame" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindMainWindow, "vsWindowKindMainWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindFindResults2, "vsWindowKindFindResults2" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindFindResults1, "vsWindowKindFindResults1" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindFindReplace, "vsWindowKindFindReplace" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindFindSymbolResults, "vsWindowKindFindSymbolResults" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindFindSymbol, "vsWindowKindFindSymbol" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindCommandWindow, "vsWindowKindCommandWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindServerExplorer, "vsWindowKindServerExplorer" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsWindowKindResourceView, "vsWindowKindResourceView" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsCATIDDocument, "vsCATIDDocument" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_CallStackWindow, "vsext_wk_CallStackWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_Toolbox, "vsext_wk_Toolbox" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_TaskList, "vsext_wk_TaskList" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_vk_TextView, "vsext_vk_TextView" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_vk_Designer, "vsext_vk_Designer" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_vk_Code, "vsext_vk_Code" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_vk_Debugging, "vsext_vk_Debugging" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_vk_Primary, "vsext_vk_Primary" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_ThreadWindow, "vsext_wk_ThreadWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_LocalsWindow, "vsext_wk_LocalsWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_WatchWindow, "vsext_wk_WatchWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_ClassView, "vsext_wk_ClassView" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_ContextWindow, "vsext_wk_ContextWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_ObjectBrowser, "vsext_wk_ObjectBrowser" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_OutputWindow, "vsext_wk_OutputWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_SProjectWindow, "vsext_wk_SProjectWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_PropertyBrowser, "vsext_wk_PropertyBrowser" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_ImmedWindow, "vsext_wk_ImmedWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_wk_AutoLocalsWindow, "vsext_wk_AutoLocalsWindow" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_GUID_NewProjectWizard, "vsext_GUID_NewProjectWizard" );
			resultStr = AppendKind( resultStr, guidStr, EnvDTE.Constants.vsext_GUID_AddItemWizard, "vsext_GUID_AddItemWizard" );

			resultStr = AppendKind( resultStr, guidStr, wixProjectItemKindProjectFile, "wixProjectItemKindProjectFile" );

			resultStr = AppendKind( resultStr, guidStr, vcContextGuidVCProject, "vcContextGuidVCProject" );

			return resultStr;
		}
		private static string AppendKind( string value, string guidStr, string kindValue, string name )
		{
			if( string.Compare( guidStr, kindValue, true ) == 0 )
			{
				if( !string.IsNullOrWhiteSpace( value ) )
				{
					value += '|';
				}
				value += name;
			}
			return value;
		}
	}
}
