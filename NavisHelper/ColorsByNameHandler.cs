using System;
using System.Windows.Input;
using Autodesk.Navisworks.Api;

namespace NavisHelper
{
    public class ColorsByNameHandler : ICommand
    {
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter)
        {
            Application.Plugins.ExecuteAddInPlugin("ColorsByName.CBC");
        }
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
} 