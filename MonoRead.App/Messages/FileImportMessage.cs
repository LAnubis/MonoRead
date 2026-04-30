using System;
using System.Collections.Generic;
using System.Text;

namespace MonoRead.App.Messages
{
    public class FileImportMessage
    {
        public string SandboxPath { get; }
        public string FileName { get; }

        public FileImportMessage(string sandboxPath, string fileName)
        {
            SandboxPath = sandboxPath;
            FileName = fileName;
        }
    }
}
