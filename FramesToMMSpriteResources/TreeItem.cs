using System.ComponentModel;

namespace FramesToMMSpriteResources
{
    public enum ItemDepth
    {
        Subject = 0,
        Animation = 1,
        Frame = 2
    }

    public partial class TreeItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text { get; set; }

        public ItemDepth Depth { get; set; }

        public string CountText { get; set; }

        public int Count;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public TreeItem(string text, ItemDepth depth, int count = -1, bool isSelected = false)
        {
            Text = text;
            Depth = depth;
            Count = count;
            CountText = count.ToString();
            _isSelected = isSelected;
        }

        public TreeItem(string text, ItemDepth depth, int oldCount, int newCount, bool isSelected = false)
        {
            Text = text;
            Depth = depth;
            Count = newCount;
            CountText = /*oldCount + " → " + */newCount.ToString();
            _isSelected = isSelected;
        }
    }
}
