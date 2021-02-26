using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DokanNet;
using System.IO;
using System.Collections;

namespace ThisWillWork
{
    class File
    {
        public List<byte> bytes;
        public FileInformation info;
        
        public Dictionary<string, File> files;

        public File(FileInformation info)
        {
            this.info = info;

            if (info.Attributes == FileAttributes.Directory)
            {
                files = new Dictionary<string, File>();
            }
            if (info.Attributes == FileAttributes.Normal)
            {
                bytes = new List<byte>();
            }
        }

        public static Dictionary<string, File> FindParent(string path, Dictionary<string, File> root) 
        {
            string[] split = path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            int i = 0;
            return FindParentHelper(split, root, ref i);
        }

        private static Dictionary<string, File> FindParentHelper(string[] split, Dictionary<string, File> root, ref int i)
        {
            if (root.ContainsKey(split[i]))
            {
                i++;
                if (i == split.Length)
                {
                    return root;
                }
                else
                {
                    return FindParentHelper(split, root[split[i - 1]].files, ref i);
                }
            }
            else if (i == split.Length - 1)
                return root;

            return null;
        }

        public static Dictionary<string, File> FindFiles(string path, Dictionary<string, File> root) 
        {
            string[] split = path.Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            int i = 0;
            return FindFilesHelper(split, root, ref i);
        }

        private static Dictionary<string, File> FindFilesHelper(string[] split, Dictionary<string, File> root, ref int i)
        {
            if (root.ContainsKey(split[i]))
            {
                i++;

                if (i == split.Length)
                {
                    return root[split[i - 1]].files;
                }
                else
                {
                    return FindFilesHelper(split, root[split[i-1]].files, ref i);
                }
            }

            return null;
        }

        public static long CalculateSize(File file)
        {
            if (file.info.Attributes == FileAttributes.Normal || file.info.Attributes == FileAttributes.ReadOnly)
            {
                return file.info.Length;
            }
            else if (file.info.Attributes == FileAttributes.Directory)
            {
                long size = 0;
                CalculateSizeHelper(file.files, ref size);

                return size;
            }

            return 0;
        }

        private static void CalculateSizeHelper(Dictionary<string, File> files, ref long size)
        {
            foreach (var file in files)
            {
                if (file.Value.info.Attributes == FileAttributes.Normal)
                {
                    size += file.Value.info.Length;
                }
                else if(file.Value.info.Attributes == FileAttributes.Directory)
                {
                    CalculateSizeHelper(file.Value.files, ref size);
                }
            }
        }
    }
}
