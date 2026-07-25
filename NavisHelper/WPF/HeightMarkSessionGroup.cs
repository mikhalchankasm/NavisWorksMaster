using Autodesk.Navisworks.Api;

namespace NavisHelper.WPF
{
    internal sealed class HeightMarkSessionGroup
    {
        public string Name { get; set; }
        public ModelItemCollection Items { get; set; }
        public string Contents { get; set; }

        public int ItemCount
        {
            get { return Items == null ? 0 : Items.Count; }
        }
    }
}
