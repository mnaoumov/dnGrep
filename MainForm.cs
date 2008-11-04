using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace nGREP
{
	public partial class MainForm : Form
	{
		List<GrepSearchResult> searchResults = new List<GrepSearchResult>();

		#region States

		private bool folderSelected = false;

		public bool FolderSelected
		{
			get { return folderSelected; }
			set
			{
				folderSelected = value;
				changeState();
			}
		}

		private bool searchPatternEntered = false;

		public bool SearchPatternEntered
		{
			get { return searchPatternEntered; }
			set { searchPatternEntered = value;
				changeState();
			}
		}
		private bool replacePatternEntered = false;

		public bool ReplacePatternEntered
		{
			get { return replacePatternEntered; }
			set { replacePatternEntered = value;
				changeState();
			}
		}

		private bool filesFound = false;

		public bool FilesFound
		{
			get { return filesFound; }
			set { 
				filesFound = value;
				changeState();
			}
		}

		private bool isAllSizes = true;

		public bool IsAllSizes
		{
			get { return isAllSizes; }
			set { 
				isAllSizes = value;
				changeState();
			}
		}

		private bool isSearching = false;

		public bool IsSearching
		{
			get { return isSearching; }
			set
			{
				isSearching = value;
				changeState();
			}
		}

		private bool isReplacing = false;

		public bool IsReplacing
		{
			get { return isReplacing; }
			set
			{
				isReplacing = value;
				changeState();
			}
		}

		private void changeState()
		{
			if (FolderSelected)
			{
				if (!IsSearching && !IsReplacing && SearchPatternEntered)
					btnSearch.Enabled = true;
				if (FilesFound && !IsSearching && !IsReplacing && SearchPatternEntered && ReplacePatternEntered)
					btnReplace.Enabled = true;
			}
			else
			{
				btnSearch.Enabled = false;
				btnReplace.Enabled = false;
			}

			if (IsAllSizes)
			{
				tbFileSizeFrom.Enabled = false;
				tbFileSizeTo.Enabled = false;
			}
			else
			{
				tbFileSizeFrom.Enabled = true;
				tbFileSizeTo.Enabled = true;
			}

			if (IsSearching)
			{
				btnSearch.Enabled = false;
				btnReplace.Enabled = false;
				btnCancel.Enabled = true;
			}
			else if (IsReplacing)
			{
				btnSearch.Enabled = false;
				btnReplace.Enabled = false;
				btnCancel.Enabled = true;
			}
			else
			{
				if (SearchPatternEntered)
					btnSearch.Enabled = true;
				if (FilesFound && SearchPatternEntered && ReplacePatternEntered)
					btnReplace.Enabled = true;
				btnCancel.Enabled = false;
			}
		}

		#endregion

		public MainForm()
		{
			InitializeComponent();
			restoreSettings();
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			if (!string.IsNullOrEmpty(Properties.Settings.Default.SearchFolder))
			{
				tbFolderName.Text = Properties.Settings.Default.SearchFolder;
				FolderSelected = true;
			}
			SearchPatternEntered = !string.IsNullOrEmpty(tbSearchFor.Text);
			ReplacePatternEntered = !string.IsNullOrEmpty(tbReplaceWith.Text);

			changeState();
		}

		private void btnSelectFolder_Click(object sender, EventArgs e)
		{
			folderSelectDialog.SelectedPath = tbFolderName.Text;
			if (folderSelectDialog.ShowDialog() == DialogResult.OK &&
				Directory.Exists(folderSelectDialog.SelectedPath))
			{
				FolderSelected = true;
				tbFolderName.Text = folderSelectDialog.SelectedPath;
			}
		}

		private void btnSearch_Click(object sender, EventArgs e)
		{
			if (!IsSearching)
			{
				lblStatus.Text = "Searching...";
				IsSearching = true;
				barProgressBar.Value = 0;
				tvSearchResult.Nodes.Clear();
				workerSearcher.RunWorkerAsync();
			}
		}

		private void btnCancel_Click(object sender, EventArgs e)
		{
			if (IsSearching || IsReplacing)
			{
				GrepCore.CancelProcess = true;
			}
		}

		private void doSearch(object sender, DoWorkEventArgs e)
		{
			if (!workerSearcher.CancellationPending && !workerReplace.IsBusy)
			{
				int sizeFrom = 0;
				int sizeTo = 0;
				if (!IsAllSizes)
				{
					sizeFrom = FileUtils.ParseInt(tbFileSizeFrom.Text, 0);
					sizeTo = FileUtils.ParseInt(tbFileSizeTo.Text, 0);
				}
				string filePattern = "*.*";
				if (!string.IsNullOrEmpty(tbFilePattern.Text))
					filePattern = tbFilePattern.Text;
				string[] files = FileUtils.GetFileList(tbFolderName.Text, filePattern, cbIncludeSubfolders.Checked, 
					cbIncludeHiddenFolders.Checked, sizeFrom, sizeTo);
				GrepCore grep = new GrepCore();
				grep.ProcessedFile += new GrepCore.SearchProgressHandler(grep_ProcessedFile);
				GrepSearchResult[] results = null;
				if (rbRegexSearch.Checked)
					results = grep.SearchRegex(files, tbSearchFor.Text);
				else
					results = grep.SearchText(files, tbSearchFor.Text);

				grep.ProcessedFile -= new GrepCore.SearchProgressHandler(grep_ProcessedFile);
				searchResults = new List<GrepSearchResult>(results);
			}
		}

		void grep_ProcessedFile(object sender, GrepCore.ProgressStatus progress)
		{
			workerSearcher.ReportProgress((int)(progress.ProcessedFiles * 100 / progress.TotalFiles), progress);
		}

		private void searchProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (!GrepCore.CancelProcess)
			{
				GrepCore.ProgressStatus progress = (GrepCore.ProgressStatus)e.UserState;
				barProgressBar.Value = e.ProgressPercentage;
				lblStatus.Text = "(" + progress.ProcessedFiles + " of " + progress.TotalFiles + ")";
			}
		}

		private void searchComplete(object sender, RunWorkerCompletedEventArgs e)
		{
			if (!e.Cancelled)
			{
				lblStatus.Text = "Search Complete - " + searchResults.Count + " files found.";
			}
			else
			{
				lblStatus.Text = "Search Canceled";
			}
			barProgressBar.Value = 0;
			IsSearching = false;
			if (searchResults.Count > 0)
				FilesFound = true;
			else
				FilesFound = false;
			populateResults();
		}

		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			GrepCore.CancelProcess = true;
			if (workerSearcher.IsBusy)
				workerSearcher.CancelAsync();
			populateSettings();
			Properties.Settings.Default.Save();
		}

		private void populateSettings()
		{
			Properties.Settings.Default.SearchRegex = rbRegexSearch.Checked;
			Properties.Settings.Default.SearchText = rbTextSearch.Checked;
			Properties.Settings.Default.FilterAllSizes = rbFilterAllSizes.Checked;
			Properties.Settings.Default.FilterSpecificSize = rbFilterSpecificSize.Checked;
		}

		private void restoreSettings()
		{
			rbRegexSearch.Checked =Properties.Settings.Default.SearchRegex;
			rbTextSearch.Checked = Properties.Settings.Default.SearchText;
			rbFilterAllSizes.Checked = Properties.Settings.Default.FilterAllSizes;
			rbFilterSpecificSize.Checked = Properties.Settings.Default.FilterSpecificSize;
		}

		private void populateResults()
		{
			tvSearchResult.Nodes.Clear();
			List<string> tempExtensionList = new List<string>();
			if (searchResults == null)
				return;

			// Populate icon list
			foreach (GrepSearchResult result in searchResults)
			{
				string ext = Path.GetExtension(result.FileName);
				if (!tempExtensionList.Contains(ext))
					tempExtensionList.Add(ext);
			}
			FileIcons.LoadImageList(tempExtensionList.ToArray());
			tvSearchResult.ImageList = FileIcons.SmallIconList;

			foreach (GrepSearchResult result in searchResults)
			{
				TreeNode node = new TreeNode(Path.GetFileName(result.FileName));
				node.Tag = result.FileName;
				tvSearchResult.Nodes.Add(node);				
				string ext = Path.GetExtension(result.FileName);

				node.ImageKey = ext;
				node.SelectedImageKey = node.ImageKey;
				node.StateImageKey = node.ImageKey;
				foreach (GrepSearchResult.GrepLine line in result.SearchResults)
				{
					string lineSummary = line.LineText.Replace("\n", "").Replace("\t", "").Replace("\r", "").Trim();
					if (lineSummary.Length == 0)
						lineSummary = "<none>";
					else if (lineSummary.Length > 100)
						lineSummary = lineSummary.Substring(0, 100) + "...";
					TreeNode lineNode = new TreeNode(line.LineNumber + ": " + lineSummary);
					lineNode.ImageKey = "%line%";
					lineNode.SelectedImageKey = lineNode.ImageKey;
					lineNode.StateImageKey = lineNode.ImageKey;
					lineNode.Tag = line.LineNumber;
					node.Nodes.Add(lineNode);
				}
			}			
		}

		private void formKeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Escape)
				Close();
		}

		private void rbFilterSizes_CheckedChanged(object sender, EventArgs e)
		{
			if (rbFilterAllSizes.Checked)
				IsAllSizes = true;
			else
				IsAllSizes = false;
		}

		private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			OptionsForm options = new OptionsForm();
			options.ShowDialog();
		}

		private void openToolStripMenuItem_Click(object sender, EventArgs e)
		{
			TreeNode selectedNode = tvSearchResult.SelectedNode;
			if (selectedNode != null)
			{
				// Line was selected
				int lineNumber = 0;
				if (selectedNode.Parent != null)
				{
					if (selectedNode.Tag != null && selectedNode.Tag is int)
					{
						lineNumber = (int)selectedNode.Tag;
					}
					selectedNode = selectedNode.Parent;
				}
				if (selectedNode != null && selectedNode.Tag != null)
				{
					if (!Properties.Settings.Default.UseCustomEditor)
						System.Diagnostics.Process.Start(@"" + (string)selectedNode.Tag + "");
					else
					{
						ProcessStartInfo info = new ProcessStartInfo("cmd.exe");
						info.UseShellExecute = false;
						info.CreateNoWindow = true;
						info.Arguments = "/C " + Properties.Settings.Default.CustomEditor.Replace("%file", "\"" + (string)selectedNode.Tag + "\"").Replace("%line", lineNumber.ToString());
						System.Diagnostics.Process.Start(info);
						//System.Diagnostics.Process.Start(@"" + OptionsForm.GetEditorPath((string)selectedNode.Tag, lineNumber) + "");
					}
				}
			}
		}

		private void tvSearchResult_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Button == MouseButtons.Right)
				tvSearchResult.SelectedNode = e.Node;
		}

		private void btnReplace_Click(object sender, EventArgs e)
		{
			if (!IsReplacing && !IsSearching)
			{
				lblStatus.Text = "Replacing...";
				IsReplacing = true;
				barProgressBar.Value = 0;
				tvSearchResult.Nodes.Clear();
				workerReplace.RunWorkerAsync();
			}
		}

		private void doReplace(object sender, DoWorkEventArgs e)
		{
			if (!workerReplace.CancellationPending && !workerSearcher.IsBusy)
			{
				GrepCore grep = new GrepCore();
				grep.ProcessedFile += new GrepCore.SearchProgressHandler(grep_ProcessedFile);
				List<string> files = new List<string>();
				foreach (GrepSearchResult result in searchResults)
				{
					files.Add(result.FileName);
				}
				
				if (rbRegexSearch.Checked)
					grep.ReplaceRegex(files.ToArray(), tbFolderName.Text, tbSearchFor.Text, tbReplaceWith.Text);
				else
					grep.ReplaceText(files.ToArray(), tbFolderName.Text, tbSearchFor.Text, tbReplaceWith.Text);

				grep.ProcessedFile -= new GrepCore.SearchProgressHandler(grep_ProcessedFile);
			}
		}

		private void replaceComplete(object sender, RunWorkerCompletedEventArgs e)
		{
			if (!e.Cancelled)
			{
				lblStatus.Text = "Replace Complete.";
			}
			else
			{
				lblStatus.Text = "Replace Canceled";
			}
			barProgressBar.Value = 0;
			IsReplacing = false;
		}

		private void replaceProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			if (!GrepCore.CancelProcess)
			{
				GrepCore.ProgressStatus progress = (GrepCore.ProgressStatus)e.UserState;
				barProgressBar.Value = e.ProgressPercentage;
				lblStatus.Text = "(" + progress.ProcessedFiles + " of " + progress.TotalFiles + ")";
			}
		}

		private void textBoxTextChanged(object sender, EventArgs e)
		{
			SearchPatternEntered = !string.IsNullOrEmpty(tbSearchFor.Text);
			ReplacePatternEntered = !string.IsNullOrEmpty(tbReplaceWith.Text);
			if (sender == tbSearchFor)
			{
				FilesFound = false;
			}
		}
	}
}