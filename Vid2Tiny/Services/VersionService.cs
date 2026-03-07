using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;

namespace Vid2Tiny.Services
{
    public static class VersionService
    {
        public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    }
}
