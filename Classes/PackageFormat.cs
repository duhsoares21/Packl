using System.Collections.Generic;
using System.Security.Policy;
namespace Packl.Classes
{
    class PackageFormat
    {
        public string type { get; set; }
        public string version { get; set; }
        public string description { get; set; }
        public string homepage { get; set; }
        public string license { get; set; }
        public string url { get; set; }
        public string hash { get; set; }
        public Installer installer { get; set; }
        public List<string> dependencies { get; set; } = null;
        public string bin { get; set; }
        public AutoUpdate autoupdate { get; set; }
    }

    class Installer
    {
        public List<string> args { get; set; }
    }

    class AutoUpdate
    {
        public string url { get; set; }
    }
}

/*
 {
  "version": "2.2",
  "description": "PladooDraw - Uma aplicação de desenho inspirada pelo Paint.",
  "homepage": "https://github.com/duhsoares21/PladooDraw",
  "license": "PDSAL1.0",
  "url": "https://github.com/duhsoares21/PladooDraw/releases/download/PladooDraw2.2/PladooDraw-Scoop.exe",
  "hash": "6970f1160c23a2a9836e5b95bb94d5207ac3670a7c0fb63e59f43a1e5a261f26",
  "installer": {
    "args": [
      "/VERYSILENT",
      "/NORESTART"
    ]
  },
  "bin": "PladooDraw.exe",
  "autoupdate": {
    "url": "https://github.com/duhsoares21/PladooDraw/releases/download/PladooDraw$version/PladooDraw-Scoop.exe"
  }
}
 */