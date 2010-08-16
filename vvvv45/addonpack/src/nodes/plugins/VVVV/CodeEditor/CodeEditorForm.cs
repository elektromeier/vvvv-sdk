﻿using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

using VVVV.Core;
using VVVV.Core.Logging;
using VVVV.Core.Model;
using VVVV.Core.Model.CS;
using VVVV.Core.Model.FX;
using VVVV.Core.View;
using VVVV.Core.View.Table;
using VVVV.HDE.CodeEditor.ErrorView;
using VVVV.HDE.CodeEditor.LanguageBindings.CS;
using VVVV.HDE.CodeEditor.LanguageBindings.FX;
using VVVV.PluginInterfaces.V2;
using Dom = ICSharpCode.SharpDevelop.Dom;
using SD = ICSharpCode.TextEditor.Document;

namespace VVVV.HDE.CodeEditor
{
	/// <summary>
	/// Sharpdevelop texteditor needs a Form as one of its parent controls
	/// in order to work properly.
	/// </summary>
	public partial class CodeEditorForm : Form, IDocumentLocator
	{
		private Dictionary<ITextDocument, TabPage> FOpenedDocuments;
		private IHDEHost FHDEHost;
		private INodeSelectionListener FNodeSelectionListener;
		private Dictionary<IProject, Dom.DefaultProjectContent> FProjects;
		private Dictionary<ITextDocument, Dom.ParseInformation> FParseInfos;
		private Dictionary<SD.IDocument, ITextDocument> FSDDocToDocMap;
		private ISolution FSolution;
		private ILogger FLogger;
		
		public CodeEditorForm(IHDEHost host, ISolution solution, ILogger logger)
		{
			//
			// The InitializeComponent() call is required for Windows Forms designer support.
			//
			InitializeComponent();
			
			var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetCallingAssembly().Location), @"..\bin"));
			var provider = new SD.FileSyntaxModeProvider(path);
			SD.HighlightingManager.Manager.AddSyntaxModeFileProvider(provider);
			
			FLogger = logger;
			FSolution = solution;
			FNodeSelectionListener = new NodeSelectionListener(this);
			FProjects = new Dictionary<IProject, Dom.DefaultProjectContent>();
			FParseInfos = new Dictionary<ITextDocument, Dom.ParseInformation>();
			FSDDocToDocMap = new Dictionary<SD.IDocument, ITextDocument>();
			
			FHDEHost = host;
			FHDEHost.AddListener(FNodeSelectionListener);
			
			FOpenedDocuments = new Dictionary<ITextDocument, TabPage>();
			
			var registry = new MappingRegistry();
			registry.RegisterDefaultMapping<IEnumerable<IColumn>, ErrorCollectionColumnProvider>();
			registry.RegisterMapping<CompilerError, IEnumerable<ICell>, ErrorCellProvider>();
			registry.RegisterMapping<IEditableIDList<IReference>, ReferencesViewProvider>();
			
			FErrorTableViewer.Registry = registry;
			FErrorTableViewer.Input = Empty.Enumerable;
			
			FSolution.Projects.Added += Project_Added;
			FSolution.Projects.Removed += Project_Removed;

			foreach (var project in FSolution.Projects)
				SetupProject(project);
		}
		
		/// <summary>
		/// Opens the specified ITextDocument in a new editor.
		/// </summary>
		/// <param name="doc">The ITextDocument to open.</param>
		/// <returns>The TabPage containing the editor.</returns>
		public TabPage Open(ITextDocument doc)
		{
			if (!FOpenedDocuments.ContainsKey(doc))
			{
				var completionDataProvider = new DefaultCompletionProvider();
				SD.IFormattingStrategy formattingStrategy = new SD.DefaultFormattingStrategy();
				SD.IFoldingStrategy foldingStrategy = null;
				ILinkDataProvider linkDataProvider = null;
				IToolTipProvider toolTipProvider = null;
				
				if (doc is CSDocument)
				{
					var csDoc = doc as CSDocument;
					completionDataProvider = new CSCompletionProvider(this);
					formattingStrategy = new CSFormattingStrategy(this);
					foldingStrategy = new CSFoldingStrategy();
					linkDataProvider = new CSLinkDataProvider(this);
					toolTipProvider = new CSToolTipProvider(this);
				}
				else if (doc is FXDocument || doc is FXHDocument)
				{
					completionDataProvider = new FXCompletionProvider(this, FLogger);
					linkDataProvider = new FXLinkDataProvider();
				}
				
				var editor = new CodeEditor(
					this, 
					doc, 
					completionDataProvider,
					formattingStrategy,
					foldingStrategy,
					linkDataProvider,
					toolTipProvider
				);
				
				FSDDocToDocMap[editor.SDDocument] = doc;
				var tabPage = new TabPage(GetTabPageName(doc));
				
				FTabControl.SuspendLayout();
				
				editor.Dock = DockStyle.Fill;
				FTabControl.Controls.Add(tabPage);
				tabPage.Controls.Add(editor);
				
				FTabControl.ResumeLayout();
				
				FOpenedDocuments[doc] = tabPage;
				doc.ContentChanged += DocumentContentChangedCB;
				doc.Saved += DocumentSavedCB;
			}
			
			return FOpenedDocuments[doc];
		}

		public void Close(ITextDocument doc)
		{
			if (FOpenedDocuments.ContainsKey(doc))
			{
				var tabPage = FOpenedDocuments[doc];
				var codeEditor = tabPage.Controls[0] as CodeEditor;
				FSDDocToDocMap.Remove(codeEditor.SDDocument);
				FTabControl.Controls.Remove(tabPage);
				FOpenedDocuments.Remove(doc);
			}
		}
		
		public void BringToFront(TabPage tabPage)
		{
			FTabControl.SelectTab(tabPage);
		}

		protected string GetTabPageName(ITextDocument document)
		{
			var name = document.Mapper.Map<INamed>().Name;
			
			if (document.IsDirty)
				return name + "*";
			else
				return name;
		}
		
		void Project_CompileCompleted(IProject project, CompilerResults results)
		{
			if (results.Errors.Count > 0)
			{
				FErrorTableViewer.Input = results.Errors;
				// TODO: Find better way to calculate splitter distance
				FSplitContainer.SplitterDistance = FSplitContainer.Height - (FErrorTableViewer.RowCount + 2) * (FErrorTableViewer.RowHeight + 2);
				FSplitContainer.Panel2Collapsed = false;
			}
			else
			{
				FSplitContainer.Panel2Collapsed = true;
			}
		}
		
		void DocumentContentChangedCB(ITextDocument document, string content)
		{
			FOpenedDocuments[document].Text = GetTabPageName(document);
		}
		
		void DocumentSavedCB(object sender, EventArgs args)
		{
			var document = sender as ITextDocument;
			FOpenedDocuments[document].Text = GetTabPageName(document);
		}
		
		void Document_Removed(IViewableCollection<IDocument> collection, IDocument item)
		{
			if (item is ITextDocument)
			{
				var textDoc = item as ITextDocument;
				Close(textDoc);
			}
		}
		
		protected void SetupProject(IProject project)
		{
			project.CompileCompleted += Project_CompileCompleted;
			project.Documents.Removed += Document_Removed;
		}

		protected void TearDownProject(IProject project)
		{
			project.CompileCompleted -= Project_CompileCompleted;
			
			foreach (var doc in project.Documents)
			{
				var textDoc = doc as ITextDocument;
				if (textDoc != null)
					Close(textDoc);
			}
		}

		void Project_Added(IViewableCollection<IProject> collection, IProject project)
		{
			SetupProject(project);
		}
		
		void Project_Removed(IViewableCollection<IProject> collection, IProject project)
		{
			TearDownProject(project);
		}

		void FErrorTableViewerDoubleClick(VVVV.Core.IModelMapper sender, System.Windows.Forms.MouseEventArgs e)
		{
			var compilerError = sender.Model as CompilerError;
			
			// Find the document which caused the compiler error.
			var doc = FSolution.FindDocument(compilerError.FileName);
			
			if (doc == null || !(doc is ITextDocument))
				return;
			
			// Open the document.
			var txtDoc = doc as ITextDocument;
			var tabPage = Open(txtDoc);
			
			// Switch to newly created tab.
			FTabControl.SelectTab(tabPage);
			
			// Jump to line of error and focus the editor.
			var codeEditor = tabPage.Controls[0] as CodeEditor;
			codeEditor.JumpTo(compilerError.Line - 1);
			codeEditor.Focus();
		}
		
		void FTabControlMouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				//focus the textarea for mousescroll to work instantly after changing tabs
				FTabControl.SelectedTab.Controls[0].Focus();
			}
			if (e.Button == MouseButtons.Middle)
			{
				//note: on middle click the FTabControl.SelectedTab is not the same as after left-click
				//need to find targeted tabpage manually here:
				for (int i=0; i<FTabControl.TabPages.Count; i++)
				{
					if (FTabControl.GetTabRect(i).Contains(e.Location))
					{
						Close((FTabControl.TabPages[i].Controls[0] as CodeEditor).Document);
						break;
					}
				}
			}
		}
		
		#region IDocumentLocator
		
		public ICSharpCode.TextEditor.Document.IDocument GetSDDocument(ITextDocument document)
		{
			foreach (var entry in FSDDocToDocMap)
			{
				if (entry.Value == document)
					return entry.Key;
			}
			
			return null;
		}
		
		public ICSharpCode.TextEditor.Document.IDocument GetSDDocument(string filename)
		{
			var doc = FSolution.FindDocument(filename);
			if (doc != null && doc is ITextDocument)
				return GetSDDocument(doc as ITextDocument);
			
			return null;
		}
		
		public ITextDocument GetVDocument(ICSharpCode.TextEditor.Document.IDocument document)
		{
			if (FSDDocToDocMap.ContainsKey(document))
				return FSDDocToDocMap[document];
			
			return null;
		}
		
		public ITextDocument GetVDocument(string filename)
		{
			var doc = FSolution.FindDocument(filename);
			if (doc != null && doc is ITextDocument)
				return doc as ITextDocument;
			
			return null;
		}
		
		#endregion
		
		#region IDisposable
		private bool FDisposed = false;
		// Dispose(bool disposing) executes in two distinct scenarios.
		// If disposing equals true, the method has been called directly
		// or indirectly by a user's code. Managed and unmanaged resources
		// can be disposed.
		// If disposing equals false, the method has been called by the
		// runtime from inside the finalizer and you should not reference
		// other objects. Only unmanaged resources can be disposed.
		protected override void Dispose(bool disposing)
		{
			// Check to see if Dispose has already been called.
			if(!FDisposed)
			{
				if(disposing)
				{
					// Dispose managed resources.
					if (FProjects != null)
					{
						foreach (var project in FProjects.Keys)
						{
							project.Documents.Removed -= Document_Removed;
						}
						FProjects.Clear();
					}
					
					if (FHDEHost != null)
					{
						if (FSolution != null)
							FSolution.Projects.Removed -= Project_Removed;
						
						FHDEHost.RemoveListener(FNodeSelectionListener);
					}
					FNodeSelectionListener = null;
					
					foreach (var entry in FOpenedDocuments)
					{
						entry.Key.ContentChanged -= DocumentContentChangedCB;
						entry.Key.Saved -= DocumentSavedCB;
					}
					
					FOpenedDocuments = null;
					
					if (components != null) {
						components.Dispose();
					}
					components = null;
				}
				// Release unmanaged resources. If disposing is false,
				// only the following code is executed.
			}
			FDisposed = true;
			
			base.Dispose(disposing);
		}
		#endregion IDisposable
	}
}