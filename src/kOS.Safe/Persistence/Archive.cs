using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using kOS.Safe.Utilities;
using FileInfo = kOS.Safe.Encapsulation.FileInfo;

namespace kOS.Safe.Persistence
{
    public class Archive : Volume
    {
        private static string ArchiveFolder
        {
            get { return Utilities.Environment.ArchiveFolder; }
        }

        public Archive()
        {
            Directory.CreateDirectory(ArchiveFolder);
            Renameable = false;
            Name = "Archive";
        }

        public override bool IsRoomFor(ProgramFile newFile)
        {
            return true;
        }

        /// <summary>
        /// Get a file given its name
        /// </summary>
        /// <param name="name">filename to get.  if it has no filename extension, one will be guessed at, ".ks" usually.</param>
        /// <param name="timeStampFirst">Is the timestamp more important than the extension (should it go for the newer file first?)</param>
        /// <returns>the file</returns>
        public override ProgramFile GetByName(string name, bool timeStampFirst = false )
        {
            try
            {
                Debug.Logger.Log("Archive: Getting File By Name: " + name);
                var fileInfo = FileSearch(name, timeStampFirst);
                if (fileInfo == null)
                {
                    return null;
                }

                using (var infile = new BinaryReader(File.Open(fileInfo.FullName, FileMode.Open)))
                {
                    byte[] fileBody = ProcessBinaryReader(infile);

                    var retFile = new ProgramFile(name);
                    FileCategory whatKind = PersistenceUtilities.IdentifyCategory(fileBody);
                    if (whatKind == FileCategory.KSM)
                        retFile.BinaryContent = fileBody;
                    else
                        retFile.StringContent = System.Text.Encoding.UTF8.GetString(fileBody);

                    if (retFile.Category == FileCategory.ASCII || retFile.Category == FileCategory.KERBOSCRIPT)
                        retFile.StringContent = retFile.StringContent.Replace("\r\n", "\n");

                    //TODO:Chris eraseme
                    //  |
                    //  |
                    //  `--- Actually I seem to have gotten it working such that it expects this DeleteByName to be here now and depends on it - steven
                    //
                    base.DeleteByName(name);

                    base.Add(retFile);

                    return retFile;
                }
            }
            catch (Exception e)
            {
                Debug.Logger.Log(e);
                return null;
            }
        }

        public override bool SaveFile(ProgramFile file)
        {
            base.SaveFile(file);

            Directory.CreateDirectory(ArchiveFolder);

            try
            {
                Debug.Logger.Log("Archive: Saving File Name: " + file.Filename);
                byte[] fileBody;
                string fileExtension;
                switch (file.Category)
                {
                    case FileCategory.OTHER:
                    case FileCategory.TOOSHORT:
                    case FileCategory.ASCII:
                    case FileCategory.KERBOSCRIPT:
                        string tempString = file.StringContent;
                        if (Utilities.Environment.IsWindows)
                        {
                            // Only evil windows gets evil windows line breaks, and only if this is some sort of ascii:
                            tempString = tempString.Replace("\n", "\r\n");
                        }
                        fileBody = System.Text.Encoding.UTF8.GetBytes(tempString.ToCharArray());
                        fileExtension = KERBOSCRIPT_EXTENSION;
                        break;
                    case FileCategory.KSM:
                        fileBody = file.BinaryContent;
                        fileExtension = KOS_MACHINELANGUAGE_EXTENSION;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                var fileName = string.Format("{0}{1}", ArchiveFolder, PersistenceUtilities.CookedFilename(file.Filename, fileExtension, true));
                Debug.Logger.Log("Archive: eraseme: trying to log to a file called \""+fileName+"\".");
                using (var outfile = new BinaryWriter(File.Open(fileName, FileMode.Create)))
                {
                    Debug.Logger.Log("Archive: eraseme:   about to run Write(\""+fileBody+"\").");
                    outfile.Write(fileBody);
                }
            }
            catch (Exception e)
            {
                Debug.Logger.Log(e);
                return false;
            }

            return true;
        }

        public override bool DeleteByName(string name)
        {
            try
            {
                Debug.Logger.Log("Archive: Deleting File Name: " + name);
                var fullPath = FileSearch(name);
                Debug.Logger.Log("Archive:  eraseme: Deleting full path name: " + (fullPath==null ? "<null>" : fullPath.FullName));
                if (fullPath == null)
                {
                    return false;
                }
                Debug.Logger.Log("Archive:  eraseme: Calling base delete full path name.");
                base.DeleteByName(name);
                File.Delete(fullPath.FullName);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }


        public override bool RenameFile(string name, string newName)
        {
            try
            {
                Debug.Logger.Log(string.Format("Archive: Renaming: {0} To: {1}", name, newName));
                var fullSourcePath = FileSearch(name);
                if (fullSourcePath == null)
                {
                    return false;
                }

                string destinationPath = string.Format(ArchiveFolder + newName);
                if (!Path.HasExtension(newName))
                {
                    destinationPath += fullSourcePath.Extension;
                }

                File.Move(fullSourcePath.FullName, destinationPath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override List<FileInfo> GetFileList()
        {
            var retList = new List<FileInfo>();

            try
            {
                Debug.Logger.Log(string.Format("Archive: Listing Files"));
                var kosFiles = Directory.GetFiles(ArchiveFolder);
                retList.AddRange(kosFiles.Select(file => new System.IO.FileInfo(file)).Select(sysFileInfo => new FileInfo(sysFileInfo)));
            }
            catch (DirectoryNotFoundException)
            {
            }

            return retList;
        }

        public override float RequiredPower()
        {
            const int MULTIPLIER = 5;
            const float POWER_REQUIRED = BASE_POWER*MULTIPLIER;

            return POWER_REQUIRED;
        }

        /// <summary>
        /// Get the file from the OS.
        /// </summary>
        /// <param name="name">filename to look for</param>
        /// <param name="timeStampFirst">in the case of having to guess which file to pick because there's more than 1 extension,
        /// if this is true, it will pick the newer one, else it will decide on the KS file over the KSM file.</param>
        /// <returns></returns>
        private System.IO.FileInfo FileSearch(string name, bool timeStampFirst = false)
        {
            var path = ArchiveFolder + name;
            if (Path.HasExtension(path))
            {
                return File.Exists(path) ? new System.IO.FileInfo(path) : null;
            }
            var kerboscriptFile = new System.IO.FileInfo(PersistenceUtilities.CookedFilename(path, KERBOSCRIPT_EXTENSION, true));
            var kosMlFile = new System.IO.FileInfo(PersistenceUtilities.CookedFilename(path, KOS_MACHINELANGUAGE_EXTENSION, true));

            if (kerboscriptFile.Exists && kosMlFile.Exists && timeStampFirst)
            {
                return kerboscriptFile.LastWriteTime > kosMlFile.LastWriteTime
                    ? kerboscriptFile
                    : kosMlFile;
            }
            if (kerboscriptFile.Exists)
            {
                return kerboscriptFile;
            }
            if (kosMlFile.Exists)
            {
                return kosMlFile;
            }
            return null;
        }

        private byte[] ProcessBinaryReader(BinaryReader infile)
        {
            const int BUFFER_SIZE = 4096;
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[BUFFER_SIZE];
                int count;
                while ((count = infile.Read(buffer, 0, buffer.Length)) != 0)
                    ms.Write(buffer, 0, count);
                return ms.ToArray();
            }
        }
    }
}
