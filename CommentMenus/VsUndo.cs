using Microsoft.VisualStudio.Shell;
using System;

namespace CommentMenus
{
	class VsUndo : IDisposable
	{
		public VsUndo(EnvDTE.UndoContext uc, string undoName )
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			disposedValue = false;
			undoContext = uc;
			if( undoContext.IsOpen == false )
			{
				undoContext.Open(undoName);
				disposedValue = true;
			}
		}

		#region IDisposable Support
		private bool disposedValue = false; // 重複する呼び出しを検出するには
		private EnvDTE.UndoContext undoContext;

		protected virtual void Dispose(bool disposing)
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			if( disposedValue)
			{
				undoContext.Close();
				disposedValue = true;
			}
		}
		// このコードは、破棄可能なパターンを正しく実装できるように追加されました。
		public void Dispose()
		{
			ThreadHelper.ThrowIfNotOnUIThread();
			// このコードを変更しないでください。クリーンアップ コードを上の Dispose(bool disposing) に記述します。
			Dispose( true);
		}
		#endregion
	}
}
