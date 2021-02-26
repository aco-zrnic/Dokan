using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;
using DokanNet;

namespace ThisWillWork
{
    class MyFileSystem : IDokanOperations
    {
        Dictionary<string, File> root;

        private int maxFiles = 16;
        private int maxFileSize = 33554432; //32MB
        private long totalBytesWritten = 0;
        private int maxDepth = 12;
        private int allowedExtensionLength = 3 + 1; //Includes the '.' ,  ".txt" is an allowed extension
        private int maxFileNameLength = 25;

        public MyFileSystem()
        {
            root = new Dictionary<string, File>();
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> infoList, IDokanFileInfo info)
        {
            infoList = new List<FileInformation>();

            if (fileName == "\\")
            {
                foreach (var file in root)
                {
                    FileInformation fileInfo = new FileInformation();
                    fileInfo = file.Value.info;
                    fileInfo.FileName = Path.GetFileName(fileInfo.FileName);
                    infoList.Add(fileInfo);
                }
            }
            else
            {
                Dictionary<string, File> files = File.FindFiles(fileName, root);

                if (files != null)
                {
                    foreach (var f in files)
                    {
                        FileInformation fileInfo = new FileInformation();
                        fileInfo = f.Value.info;
                        fileInfo.FileName = Path.GetFileName(fileInfo.FileName);
                        infoList.Add(fileInfo);
                    }
                }
            }


            return NtStatus.Success;
        }

        public NtStatus CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            if (fileName == "\\") return NtStatus.Success;

            if (mode == FileMode.Open)
            {
                Dictionary<string, File> files = File.FindParent(fileName, root);

                if (files == null) return NtStatus.Error;
                else if (files.ContainsKey(Path.GetFileName(fileName)))
                {
                    files[Path.GetFileName(fileName)].info.LastAccessTime = DateTime.Now;
                    return NtStatus.Success;
                }

                return NtStatus.Success;
            }

            if (mode == FileMode.CreateNew)
            {
                return CreateFileHelper(fileName, access, share, mode, options, attributes, info);
            }

            if (mode == FileMode.OpenOrCreate)
            {
                Dictionary<string, File> files = File.FindParent(fileName, root);

                if (files == null) return NtStatus.Error;
                else if (files.ContainsKey(Path.GetFileName(fileName)))
                {
                    return NtStatus.Success;
                }

                return CreateFileHelper(fileName, access, share, mode, options, attributes, info);
            }
            return NtStatus.Error;
        }

        private NtStatus CreateFileHelper(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            FileInformation fileInfo = new FileInformation();
            fileInfo.FileName = fileName;
            fileInfo.CreationTime = DateTime.Now;
            fileInfo.LastWriteTime = DateTime.Now;
            fileInfo.LastAccessTime = DateTime.Now;
            fileInfo.Length = 0;

            if (info.IsDirectory)
                fileInfo.Attributes = FileAttributes.Directory;
            else
            {
                fileInfo.Attributes = FileAttributes.Normal;
            }

            File file = new File(fileInfo);

            Dictionary<string, File> listOfFiles = File.FindParent(fileName, root);

            if (listOfFiles == null) return NtStatus.Error;

            if (listOfFiles.Count >= maxFiles) return NtStatus.Error;

            if (Path.GetFileNameWithoutExtension(fileName).Length > maxFileNameLength) return NtStatus.Error;

            if (Path.HasExtension(fileName))
            {
                Console.WriteLine(Path.GetExtension(fileName));
                if (Path.GetExtension(fileName).Length != allowedExtensionLength) return NtStatus.Error;
            }

            if (fileName.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).Length >= maxDepth + 1) { return NtStatus.Error; }

            listOfFiles.Add(Path.GetFileName(fileName), file);

            return NtStatus.Success;
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            fileInfo = new FileInformation();
            fileInfo.FileName = fileName;

            if (fileName == "\\")
            {
                fileInfo.Attributes = FileAttributes.Directory;
                fileInfo.LastAccessTime = DateTime.Now;
                fileInfo.LastWriteTime = null;
                fileInfo.CreationTime = null;

                return DokanResult.Success;
            }

            else
            {
                Dictionary<string, File> files = File.FindParent(fileName, root);

                if (files != null)
                {
                    if (files.ContainsKey(Path.GetFileName(fileName)))
                    {
                        fileInfo = files[Path.GetFileName(fileName)].info;
                        return DokanResult.Success;
                    }
                }
            }
            return DokanResult.Error;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            Dictionary<string, File> files = File.FindParent(fileName, root);
            bytesWritten = 0;

            if (files != null)
            {
                if(files.ContainsKey(Path.GetFileName(fileName)))
                {
                    if (files[Path.GetFileName(fileName)].info.Length + buffer.Length> maxFileSize) return NtStatus.FileTooLarge;
                    if (files[Path.GetFileName(fileName)].info.Attributes.ToString().Contains(FileAttributes.ReadOnly.ToString())) return NtStatus.AccessDenied;
                    List<byte> file = files[Path.GetFileName(fileName)].bytes;

                    if (offset == 0)
                    {
                        totalBytesWritten -= files[Path.GetFileName(fileName)].info.Length;
                        file.Clear();
                    }

                    int i = 0;
                    for (i = 0; i < buffer.Length; ++i)
                        file.Add(buffer[i]);

                    files[Path.GetFileName(fileName)].info.Length = file.Count;
                    files[Path.GetFileName(fileName)].info.LastWriteTime = DateTime.Now;
                    files[Path.GetFileName(fileName)].bytes = file;
                    bytesWritten = i;

                    totalBytesWritten += i;
                    return NtStatus.Success;
                }
            }

            return NtStatus.Error;
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            Object readlock = new object();

            Dictionary<string, File> files = File.FindParent(fileName, root);

            bytesRead = 0;

            if (files != null)
            {
                if (files.ContainsKey(Path.GetFileName(fileName)) && files[Path.GetFileName(fileName)].bytes != null)
                {
                    if (offset + buffer.Length > files[Path.GetFileName(fileName)].info.Length)
                    {
                        offset = 0;
                    }

                    List<byte> file = files[Path.GetFileName(fileName)].bytes;

                    int i = 0;
                    for (i = 0; i < file.Count && i < buffer.Length; ++i)
                    {
                        buffer[i] = file[i + (int)offset];
                    }

                    bytesRead = i;

                    return NtStatus.Success;
                }
            }
            return NtStatus.Error;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            Dictionary<string, File> oldFiles = File.FindParent(oldName, root);
            Dictionary<string, File> newFiles = File.FindParent(newName, root);

            if (Path.HasExtension(newName))
            {
                Console.WriteLine(Path.GetExtension(newName));
                if (Path.GetExtension(newName).Length != allowedExtensionLength) return NtStatus.Error;
            }

            if (Path.GetFileNameWithoutExtension(newName).Length > maxFileNameLength) return NtStatus.Error;

            if (oldFiles != null && newFiles != null)
            {
                File file = oldFiles[Path.GetFileName(oldName)];
                file.info.FileName = Path.GetFileName(newName);

                oldFiles.Remove(Path.GetFileName(oldName));

                newFiles.Add(Path.GetFileName(newName),file);
                    return NtStatus.Success;
            }

            return NtStatus.Error;
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            Dictionary<string, File> files = File.FindParent(fileName, root);

            if (files != null)
            {
                if (files.ContainsKey(Path.GetFileName(fileName)))
                {
                    return NtStatus.Success;
                }
            }
            return NtStatus.Error;
        }

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            Dictionary<string, File> files = File.FindParent(fileName, root);

            if (files != null)
            {
                if (files.ContainsKey(Path.GetFileName(fileName)))
                {
                    return NtStatus.Success;
                }
            }
            return NtStatus.Error;
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            if (info.DeleteOnClose)
            {
                Dictionary<string, File> files = File.FindParent(fileName, root);
                totalBytesWritten -= File.CalculateSize(files[Path.GetFileName(fileName)]);
                files.Remove(Path.GetFileName(fileName));
            }
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = null;
            return NtStatus.Success;
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            totalNumberOfBytes = 536870912;
            totalNumberOfFreeBytes = freeBytesAvailable = totalNumberOfBytes - totalBytesWritten;
            return NtStatus.Success;
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = null;
            return DokanResult.NotImplemented;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            maximumComponentLength = 255;
            features = new FileSystemFeatures();
            fileSystemName = "MyFileSystem";
            volumeLabel = "New Disk";
            return NtStatus.Success;
        }

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            try
             {
                 ((FileStream)(info.Context)).Lock(offset, length);
                 return DokanResult.Success;
             }
             catch (IOException)
             {
                 return DokanResult.AccessDenied;
             }
        }

        public NtStatus Mounted(IDokanFileInfo info)
        {
            return DokanResult.NotImplemented;
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            return DokanResult.NotImplemented;
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            try
            {
                if (attributes != 0)
                {
                    Dictionary<string, File> files = File.FindParent(fileName, root);

                    if (files == null) return NtStatus.Error;
                    else if (files.ContainsKey(Path.GetFileName(fileName)))
                    {
                        files[Path.GetFileName(fileName)].info.Attributes = attributes;
                    }
                }
                return DokanResult.Success;
            }
              catch (UnauthorizedAccessException)
            {
                return DokanResult.AccessDenied;
            }
            catch (FileNotFoundException)
            {
                return DokanResult.FileNotFound;
            }
            catch (DirectoryNotFoundException)
            {
                return DokanResult.PathNotFound;
            }
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return DokanResult.NotImplemented;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Unlock(offset, length);
                return DokanResult.Success;
            }
            catch (IOException)
            {
                return DokanResult.AccessDenied;
            }
        }

        public NtStatus Unmounted(IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {

        }
    }
}
