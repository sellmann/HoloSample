using System;
using Windows.ApplicationModel.Core;

namespace HoloSample
{
    /// <summary>
    /// Windows Holographic application using SharpDX.
    /// </summary>
    internal class Program
    {
        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            var exclusiveViewApplicationSource = new AppViewSource();
            CoreApplication.Run(exclusiveViewApplicationSource);
        }
    }
}