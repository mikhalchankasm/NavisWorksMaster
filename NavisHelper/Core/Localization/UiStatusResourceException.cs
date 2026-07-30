using System;

namespace NavisHelper.Core.Localization
{
    internal sealed class UiStatusResourceException : Exception
    {
        internal UiStatusResourceException(UiStatusResourceDescriptor descriptor)
            : base("A structured UI operation failed.")
        {
            Descriptor = descriptor ??
                         throw new ArgumentNullException(nameof(descriptor));
        }

        internal UiStatusResourceDescriptor Descriptor { get; }
    }
}
