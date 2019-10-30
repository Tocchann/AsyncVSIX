using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UploadSln
{
	/// <remarks/>
	[System.SerializableAttribute()]
	[System.ComponentModel.DesignerCategoryAttribute( "code" )]
	[System.Xml.Serialization.XmlTypeAttribute( AnonymousType = true, Namespace = "UploadInfo.xsd" )]
	[System.Xml.Serialization.XmlRootAttribute( Namespace = "UploadInfo.xsd", IsNullable = false )]
	public partial class UploadInfo
	{
		private UploadInfoTarget targetField;
		private UploadInfoExclude[] excludeField;
		/// <remarks/>
		public UploadInfoTarget Target
		{
			get
			{
				return this.targetField;
			}
			set
			{
				this.targetField = value;
			}
		}
		/// <remarks/>
		[System.Xml.Serialization.XmlElementAttribute( "Exclude" )]
		public UploadInfoExclude[] Exclude
		{
			get
			{
				return this.excludeField;
			}
			set
			{
				this.excludeField = value;
			}
		}
		public bool IsExclude( string filePath )
		{
			if( Exclude == null )
			{
				return false;
			}
			//	パスに文字列を含んでいるかを判定(大文字小文字は区別する)
			var detectCount = Exclude.Count( x => filePath.Contains( x.File ) );
			return detectCount > 0;
		}
	}

	/// <remarks/>
	[System.SerializableAttribute()]
	[System.ComponentModel.DesignerCategoryAttribute( "code" )]
	[System.Xml.Serialization.XmlTypeAttribute( AnonymousType = true, Namespace = "UploadInfo.xsd" )]
	public partial class UploadInfoTarget
	{
		private string targetIDField;
		private string folderField;
		/// <remarks/>
		[System.Xml.Serialization.XmlAttributeAttribute()]
		public string TargetID
		{
			get
			{
				return this.targetIDField;
			}
			set
			{
				this.targetIDField = value;
			}
		}
		/// <remarks/>
		[System.Xml.Serialization.XmlAttributeAttribute()]
		public string Folder
		{
			get
			{
				return this.folderField;
			}
			set
			{
				this.folderField = value;
			}
		}
	}
	/// <remarks/>
	[System.SerializableAttribute()]
	[System.ComponentModel.DesignerCategoryAttribute( "code" )]
	[System.Xml.Serialization.XmlTypeAttribute( AnonymousType = true, Namespace = "UploadInfo.xsd" )]
	public partial class UploadInfoExclude
	{
		private string fileField;
		/// <remarks/>
		[System.Xml.Serialization.XmlAttributeAttribute()]
		public string File
		{
			get
			{
				return this.fileField;
			}
			set
			{
				this.fileField = value;
			}
		}
	}
}
