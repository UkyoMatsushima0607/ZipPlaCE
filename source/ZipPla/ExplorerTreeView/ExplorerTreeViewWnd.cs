using System;
using System.Collections;
using System.Windows.Forms;

namespace WilsonProgramming
{
    class ExplorerTreeViewWnd : TreeView
    {
        // TreeView は DoubleBuffered プロパティは効果がない
        /*
        public ExplorerTreeViewWnd() : base()
        {
            DoubleBuffered = true;
        }
        */

        // 解決策
        protected override CreateParams CreateParams
        {
            get
            {
                if (DesignMode)
                {
                    return base.CreateParams;
                }
                var c = base.CreateParams;
                const int WS_EX_COMPOSITED = 0x2000000;
                c.ExStyle |= WS_EX_COMPOSITED;
                return c;
            }
        }

        protected override void OnBeforeExpand(TreeViewCancelEventArgs e)
        {
            RefreshNode(e.Node);
            base.OnBeforeExpand(e);
        }
        
        public void ExpandWithoutRefresh(TreeNode node)
        {
            var temp = RefreshNode_Stop;
            RefreshNode_Stop = true;
            node.Expand();
            RefreshNode_Stop = temp;
        }
        
        public void RefreshNodeGently(TreeNode node)
        {
            if (RefreshNode_Stop) return;
            RefreshNode_Stop = true;
            try
            {
                RefreshNodeGentlyCore(node);
            }
            finally
            {
                RefreshNode_Stop = false; // 例外で true のまま残ると、以後ツリーが更新されなくなっていた（K22 で判明）
            }
        }

        private void RefreshNodeGentlyCore(TreeNode node)
        {
            var newChildren = GetChildren(node);
            var newCount = newChildren.Length;
            var nodes = node.Nodes;
            var curCount = nodes.Count;
            var curChildren = new ShellItem[curCount];
            var curIndex = 0;
            foreach (TreeNode child in nodes)
            {
                curChildren[curIndex++] = ((ShellItem)child.Tag);
            }

            curIndex = 0;
            var newIndex = 0;
            var phyIndex = 0;
            
            while (curIndex < curCount && newIndex < newCount)
            {
                var comp = newChildren[newIndex].CompareTo(curChildren[curIndex]);
                if (comp == 0)
                {
                    CopyHasSubFolder(nodes, phyIndex++, curChildren[curIndex++], newChildren[newIndex++]);
                }
                else if (comp > 0)
                {
                    curIndex++;
                    var node1 = nodes[phyIndex];
                    if (node1.IsExpanded) OnBeforeCollapse(new TreeViewCancelEventArgs(node1, false, TreeViewAction.Collapse));
                    nodes.RemoveAt(phyIndex);
                }
                else
                {
                    nodes.Insert(phyIndex++, CreateNode(newChildren[newIndex++]));
                }
            }

            if (newIndex < newCount)
            {
                var newNodes = new TreeNode[newCount - newIndex];
                var start = newIndex;
                while (newIndex < newCount)
                {
                    newNodes[newIndex - start] = CreateNode(newChildren[newIndex++]); // 以前は newIndex - newCount（負の添字）で例外になっていた（K22 で判明）
                }
                nodes.AddRange(newNodes);
            }
            else
            {
                while (curIndex < curCount)
                {
                    curIndex++;
                    var node1 = nodes[phyIndex];
                    if (node1.IsExpanded) OnBeforeCollapse(new TreeViewCancelEventArgs(node1, false, TreeViewAction.Collapse));
                    nodes.RemoveAt(phyIndex);
                }
            }
        }

        /// <summary>
        /// 子フォルダ childPath の「子フォルダがあるか」（展開の＋）だけを更新する（K22）。子の中身が変わっただけのときに、
        /// 親の子を全部列挙し直す（RefreshNodeGently）のを避ける。has はワーカースレッドで調べた値
        /// </summary>
        public void UpdateHasSubFolder(TreeNode parent, string childPath, bool has)
        {
            if (RefreshNode_Stop) return;
            foreach (TreeNode child in parent.Nodes)
            {
                var item = child.Tag as ShellItem;
                if (item == null || string.Compare(item.Path, childPath, ignoreCase: true) != 0) continue;
                if (item.HasSubFolder == has || child.IsExpanded) return; // 展開中の子は自分の監視が更新する
                item.HasSubFolder = has;
                if (has)
                {
                    if (child.Nodes.Count == 0) child.Nodes.Add("PH");
                }
                else
                {
                    child.Nodes.Clear();
                }
                return;
            }
        }

        // RefreshNodeGently 専用なので OnBeforeCollapse は行わない
        private static void CopyHasSubFolder(TreeNodeCollection dstCollection, int dstIndex, ShellItem dstItem, ShellItem srcItem)
        {
            var dstHas = dstItem.HasSubFolder;
            if (dstHas != srcItem.HasSubFolder)
            {
                if (dstHas)
                {
                    dstCollection[dstIndex].Nodes.Clear();
                }
                else
                {
                    dstCollection[dstIndex].Nodes.Add("PH");
                }
                dstItem.HasSubFolder = !dstHas; // 付け外しに合わせる。戻さないと次の比較でまた付け外ししていた（K22 で判明）
            }
        }

        private bool RefreshNode_Stop = false;
        public void RefreshNode(TreeNode node)
        {
            if (RefreshNode_Stop) return;
            RefreshNode_Stop = true;

            if (node.IsExpanded) OnBeforeCollapse(new TreeViewCancelEventArgs(node, false, TreeViewAction.Collapse));

            var nodes = node.Nodes;
            
            nodes.Clear();
            
            nodes.AddRange(CreateNode(GetChildren(node)));

            RefreshNode_Stop = false;
        }
        
        private static ShellItem[] GetChildren(TreeNode node)
        {
            ShellItem shNode = (ShellItem)node.Tag;
            ArrayList arrSub = shNode.GetSubFolders();
            var result = new ShellItem[arrSub.Count];
            var i = 0;
            foreach (ShellItem shChild in arrSub)
            {
                result[i++] = shChild;
            }
            return result;
        }

        public static TreeNode[] CreateNode(ShellItem[] shItems)
        {
            var result = new TreeNode[shItems.Length];
            for (var i = 0; i < shItems.Length; i++)
            {
                result[i] = CreateNode(shItems[i]);
            }
            return result;
        }

        public static TreeNode CreateNode(ShellItem shItem)
        {
            var node = new TreeNode();
            node.Text = shItem.DisplayName;
            node.ImageIndex = shItem.IconIndex;
            node.SelectedImageIndex = shItem.IconIndex;
            node.Tag = shItem;
            // If this is a folder item and has children then add a place holder node.
            if (shItem.IsFolder && shItem.HasSubFolder) node.Nodes.Add("PH");
            return node;
        }
    }
}
