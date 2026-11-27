using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ZipPla
{
    public class GraphicalListDragEventArgs : DragEventArgs
    {
        public Keys GetKeyState()
        {
            return (Keys)KeyState;
        }
        //int x, y;

        private int index;
        public int Index { get { return index; } }

        public GraphicalListDragEventArgs(DragEventArgs e, int index = -1/*, Action finallyAction = null*/) : base(e.Data, e.KeyState, e.X, e.Y, e.AllowedEffect, e.Effect)
        {
            this.index = index;
            //this.finallyAction = finallyAction;
        }


        public void SetTo(DragEventArgs e)
        {
            e.Effect = Effect;
        }

    }
    public delegate void GraphicalListDragEventHandler(IGraphicalList sender, GraphicalListDragEventArgs e);
    public interface IGraphicalList
    {
        event GraphicalListDragEventHandler GraphicalListDragEnter;
        event GraphicalListDragEventHandler GraphicalListDragOver;
        event EventHandler GraphicalListDragLeave;
        event GraphicalListDragEventHandler GraphicalListDragDrop;

        void DrawHighlight(int index);
        void Invalidate(int index);
    }
}
