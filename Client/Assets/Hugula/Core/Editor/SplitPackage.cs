﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

using Hugula;
using Hugula.Utils;
using Hugula.Update;

namespace Hugula.Editor
{

    /// <summary>
    /// Split package.
    /// </summary>
    public class SplitPackage
    {

        public const string VerExtends = EditorCommon.VerExtends;//"VerExtends.txt";
        public const string VerExtendsPath = EditorCommon.ConfigPath;// "Assets/Hugula/Config/";

        public const string ResFolderName = EditorCommon.ResFolderName;//"res";
        #region public


        /// <summary>
        /// 1 读取首包，找出忽略文件
        /// </summary>
        /// <param name="ignoreFiles">Ignore files.</param>
        public static bool ReadFirst(Dictionary<string, object[]> firstCrcDict, HashSet<string> manualFileList)
        {
            string title = "read first crc file list";
            CrcCheck.Clear();
            bool firstExists = false;

            string readPath = Path.Combine(GetFirstOutPath(), CUtils.platform);
            string firstFileName = CUtils.InsertAssetBundleName(CUtils.GetRightFileName(Common.CRC32_FILELIST_NAME),"_v"+CodeVersion.CODE_VERSION.ToString());
            readPath = Path.Combine(readPath,firstFileName);
            Debug.Log(readPath);

            manualFileList.Clear();
            //读取首包
            WWW abload = new WWW("file://" + readPath);
            if (string.IsNullOrEmpty(abload.error) && abload.assetBundle != null)
            {
                var ab = abload.assetBundle;
                Object[] assets = ab.LoadAllAssets();
                BytesAsset ba;
                foreach(Object o in assets)
                {
                     ba = o as BytesAsset;
                     if(ba!=null)
                     {
                        byte[] bytes = ba.bytes;
                        string context = LuaHelper.GetUTF8String(bytes);
                        Debug.Log(context);
                        string[] split = context.Split('\n');
                        System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex(@"\[""(.+)""\]\s+=\s+{(\d+),(\d+)}");
                        float j = 1;
                        float l = split.Length;
                        foreach (var line in split)
                        {
                            System.Text.RegularExpressions.Match match = regex.Match(line);
                            if (match.Success)
                            {
                                //Debug.Log(match.Groups[1].Value + " " + match.Groups[2].Value);
                                object[] val = new object[] { System.Convert.ToUInt32(match.Groups[2].Value), System.Convert.ToUInt32(match.Groups[3].Value) };
                                firstCrcDict[match.Groups[1].Value] = val;
                            }
                            //Debug.Log(line);
                            EditorUtility.DisplayProgressBar(title, "read first crc => " + j.ToString() + "/" + l.ToString(), j / l);
                            j++;
                        }    
                        firstExists = true;
                     }
                }
                ab.Unload(true);
            }
            else
            {
                Debug.LogWarning(abload.error + "no frist packeage in " + readPath);
            }
            abload.Dispose();

            //读取忽略扩展包
            bool spExtFolder = HugulaSetting.instance.spliteExtensionFolder;
            if(spExtFolder)
            {
                //
                string firstStreamingPath = CUtils.realStreamingAssetsPath;
                //读取忽略扩展文件夹
                DirectoryInfo dinfo = new DirectoryInfo(firstStreamingPath);
                var dircs = dinfo.GetDirectories();
                foreach (var dir in dircs)
                {
                    var u3dList = ExportResources.getAllChildFiles(dir.FullName, @"\.meta$|\.manifest$|\.DS_Store$", null, false);
                    //List<string> assets = new List<string>();
                    foreach (var s in u3dList)
                    {
                        string ab = CUtils.GetAssetBundleName(s);
                        ab = ab.Replace("\\", "/");
                        manualFileList.Add(ab);
                        Debug.Log("extends folder:" + ab);
                    }
                }

                 //读取忽略别名后缀
                var inclusionVariants = HugulaSetting.instance.inclusionVariants;
                var allVariants = HugulaSetting.instance.allVariants;
                string pattern = "";
                string sp = "";
                foreach(var s in allVariants)
                {
                    if(!inclusionVariants.Contains(s))
                    {
                        pattern += sp+@"\."+s+"$";
                        sp = "|";
                    }
                }
                
                if(!string.IsNullOrEmpty(pattern))
                {
                    Debug.Log(pattern);
                    var u3dList = ExportResources.getAllChildFiles(dinfo.FullName, pattern, null, true);
                    foreach (var s in u3dList)
                    {
                        string ab = CUtils.GetAssetBundleName(s);
                        ab = ab.Replace("\\", "/");
                        manualFileList.Add(ab);
                        Debug.Log("inclusionVariants " + ab);
                    }
                }

                var extensionFiles = HugulaExtensionFolderEditor.instance.ExtensionFiles;
                foreach(var s  in extensionFiles)
                {
                     manualFileList.Add(s);
                    Debug.Log("extensionFile: " + s);
                }

            }else
            {
                Debug.Log("extends folder is close ,spliteExtensionFolder=" + spExtFolder);
            }

           


            //从网络读取扩展加载列表 todo


            EditorUtility.ClearProgressBar();
            return firstExists;
        }

        /// <summary>
        /// 2 Creates the content of the crc list.
        /// </summary>
        /// <returns>The crc list content.</returns>
        /// <param name="allBundles">All bundles.</param>
        /// <param name="manualFileList">manual file list.</param>
        public static StringBuilder[] CreateCrcListContent(string[] allBundles, Dictionary<string, object[]> firstCrcDict, Dictionary<string, object[]> currCrcDict, Dictionary<string, object[]> diffCrcDict, HashSet<string> manualFileList)
        {
            string title = "create crc list content ";
            StringBuilder[] sbs = new StringBuilder[2];
            sbs[0] = new StringBuilder();
            sbs[1] = new StringBuilder();

            //var selected = string.Empty;
            float i = 0;
            float allLen = allBundles.Length;

            //group 0 manual,normal
            var manual0 = new StringBuilder();
            var normal0 = new StringBuilder();
            //group 1 manual,normal
            var manual1 = new StringBuilder();
            var normal1 = new StringBuilder();
            //忽略列表
            Dictionary<string, bool> ignore = new Dictionary<string, bool>();
            ignore.Add(CUtils.GetRightFileName(Common.CRC32_FILELIST_NAME), true);
            ignore.Add(CUtils.GetRightFileName(Common.CRC32_VER_FILENAME), true);
            CrcCheck.Clear();

            filterSB getCurrSB = (string key,StringBuilder manual, StringBuilder normal, HashSet<string> manualList) =>
            {
                if (manualList.Contains(key))
                     return manual;
                else 
                    return normal;
            };

            StringBuilder currSb;
            string extension ;
            foreach (var str in allBundles)
            {
                string url = Path.Combine(Application.dataPath, str);
                uint outCrc = 0;
                uint fileLen = 0;
                string abName = str.Replace("\\", "/");
                string key = BuildScript.GetAssetBundleName(abName);
                //后缀替换
                extension = System.IO.Path.GetExtension(key);
                if(!string.IsNullOrEmpty(extension) && extension.Equals(Common.DOT_BYTES))
                {
                    key = key.Replace(extension,Common.CHECK_ASSETBUNDLE_SUFFIX);
                }
                if (!ignore.ContainsKey(key))
                {
                    outCrc = CrcCheck.GetLocalFileCrc(url, out fileLen);
                    currCrcDict[key] = new object[] { outCrc, fileLen, str};
                    currSb = getCurrSB(key,  manual0, normal0, manualFileList);
                    currSb.AppendLine("[\"" + key + "\"] = {" + outCrc + "," + fileLen + "},");
                    object[] fCrc = null;
                    if (firstCrcDict.TryGetValue(key, out fCrc) == false || (uint)fCrc[0] != outCrc)//如果不一样
                    {
                        diffCrcDict[key] = new object[] { outCrc, fileLen , str};
                        //					Debug.LogFormat("need update abName = {0} = {1} = {2}",abName,key,outCrc);
                        currSb = getCurrSB(key,  manual1, normal1, manualFileList);
                        currSb.AppendLine("[\"" + key + "\"] = {" + outCrc + "," + fileLen + "},");
                    }
                }
                EditorUtility.DisplayProgressBar(title, title + "=>" + i.ToString() + "/" + allLen.ToString(), i / allLen);
                i++;
            }


            sbs[0].Append("return {");
            sbs[0].AppendLine("[\"manual\"] = { ");
            sbs[0].AppendLine(manual0.ToString() + "},");
            sbs[0].AppendLine("[\"normal\"] = { ");
            sbs[0].AppendLine(normal0.ToString() + "}");
            sbs[0].AppendLine("}");

            sbs[1].Append("return {");
            sbs[1].AppendLine("[\"manual\"] = { ");
            sbs[1].AppendLine(manual1.ToString() + "},");
            sbs[1].AppendLine("[\"normal\"] = { ");
            sbs[1].AppendLine(normal1.ToString() + "}");
            sbs[1].AppendLine("}");
            CrcCheck.Clear();
            EditorUtility.ClearProgressBar();
            return sbs;
        }

        /// <summary>
        /// Creates the streaming crc list.
        /// </summary>
        /// <param name="sb">Sb.</param>
        public static uint CreateStreamingCrcList(StringBuilder sb, bool firstExists = false, string outPath = null)
        {
            var crc32filename = CUtils.GetAssetName(Common.CRC32_FILELIST_NAME);
            string tmpPath = BuildScript.GetAssetTmpPath();// Path.Combine(Application.dataPath, BuildScript.TmpPath);
            ExportResources.CheckDirectory(tmpPath);
            string assetPath = "Assets/" + BuildScript.TmpPath + crc32filename + ".asset";
            EditorUtility.DisplayProgressBar("Generate streaming crc file list", "write file to " + assetPath, 0.99f);

            string outTmpPath = Path.Combine(tmpPath, crc32filename + ".lua");
            using (StreamWriter sr = new StreamWriter(outTmpPath, false))
            {
                sr.Write(sb.ToString());
            }
            //
            //打包到streaming path
            AssetDatabase.Refresh();

            BytesAsset ba = ScriptableObject.CreateInstance(typeof(BytesAsset)) as BytesAsset;
            ba.bytes = File.ReadAllBytes(outTmpPath);
            AssetDatabase.CreateAsset(ba,assetPath);

            string crc32outfilename = CUtils.GetRightFileName(Common.CRC32_FILELIST_NAME);
            Debug.Log("write to path=" + outPath);
            Debug.Log(sb.ToString());
            //读取crc
            string abPath = string.Empty;
            string resOutPath = null;
            if (string.IsNullOrEmpty(outPath))
                abPath = Path.Combine(CUtils.realStreamingAssetsPath, crc32outfilename);
            else
            {
                resOutPath = Path.Combine(outPath, ResFolderName);
                ExportResources.CheckDirectory(resOutPath);
                abPath = Path.Combine(resOutPath, crc32outfilename);
            }

            BuildScript.BuildABs(new string[] { assetPath }, resOutPath, crc32outfilename, BuildAssetBundleOptions.DeterministicAssetBundle);

            CrcCheck.Clear();
            uint fileSize = 0;
            uint fileCrc = CrcCheck.GetLocalFileCrc(abPath, out fileSize);
            EditorUtility.ClearProgressBar();

            Debug.Log("Crc file list assetbunle build complate! " + fileCrc.ToString() + abPath);
            if (!string.IsNullOrEmpty(outPath)) //copy crc list to res folder
            {
                string crc32FirstOutName = CUtils.InsertAssetBundleName(crc32outfilename,"_v"+CodeVersion.CODE_VERSION.ToString());
                string newName = Path.Combine(resOutPath, CUtils.InsertAssetBundleName(crc32outfilename, "_" + fileCrc.ToString()));
                if (File.Exists(newName)) File.Delete(newName);
                FileInfo finfo = new FileInfo(abPath);
                if (!firstExists) //如果没有首包 copy first package
                {
                    string destFirst = Path.Combine(outPath, crc32FirstOutName);
                    Debug.Log("destFirst:" + destFirst);
                    File.Copy(abPath,destFirst,true);
                }
                finfo.MoveTo(newName);
                Debug.Log(" change name to " + newName);
            }
            return fileCrc;
        }

        /// <summary>
        /// Creates the version asset bundle.
        /// </summary>
        /// <param name="fileCrc">File crc.</param>
        public static void CreateVersionAssetBundle(uint fileCrc)
        {
            //read ver extends
            string ver_file_name = VerExtends;
            ver_file_name = VerExtendsPath + CUtils.InsertAssetBundleName(ver_file_name, "_" + CUtils.platform).Replace("//", "/");

            StringBuilder verExtSB = new StringBuilder();
            if (File.Exists(ver_file_name))
            {
                using (StreamReader sr = new StreamReader(ver_file_name))
                {
                    string item;
                    while ((item = sr.ReadLine()) != null)
                    {
                        verExtSB.AppendFormat(",{0}", item);
                    }
                }

                Debug.LogFormat("read extends:{0},coutent={1}", ver_file_name, verExtSB.ToString());
            }

            string path = CUtils.GetRealStreamingAssetsPath();//Path.Combine (Application.streamingAssetsPath, CUtils.GetAssetPath(""));
            string outPath = Path.Combine(path, CUtils.GetRightFileName(Common.CRC32_VER_FILENAME));
            Debug.Log("verion to path=" + outPath);
            EditorUtility.DisplayProgressBar("Create Version AssetBundle File", "write file to " + outPath, 0.99f);
            //json 化version{ code,crc32,version}
            CodeVersion.CODE_VERSION = 0;
            CodeVersion.APP_NUMBER = 0;
            CodeVersion.RES_VERSION = 0;
            StringBuilder verJson = new StringBuilder();
            verJson.Append("{");
            verJson.Append(@"""code"":" + CodeVersion.CODE_VERSION + ",");
            verJson.Append(@"""crc32"":" + fileCrc.ToString() + ",");
            verJson.Append(@"""time"":" + CUtils.ConvertDateTimeInt(System.DateTime.Now)+",");
            verJson.Append(@"""version"":""" + CodeVersion.APP_VERSION+@"""");
            verJson.Append(verExtSB.ToString());
            verJson.Append("}");
            // platform
            using (StreamWriter sr = new StreamWriter(outPath, false))
            {
                sr.Write(verJson.ToString());
            }

            Debug.Log("Build Version Complete = " + fileCrc.ToString() + " path " + outPath);
            BuildScript.BuildAssetBundles();
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        ///   delete res folder
        /// </summary>
        public static void DeleteSplitPackageResFolder()
        {
            string updateOutPath = Path.Combine(UpdateOutPath, ResFolderName);
            ExportResources.DirectoryDelete(updateOutPath);
        }

        /// <summary>
        /// Copies the version and crc file list assetbundle to split folder.
        /// </summary>
        public static void CopyVersionToSplitFolder(uint filelistCrc)
        {
            AssetDatabase.Refresh();
            string updateOutPath = UpdateOutVersionPath; //string.Format("{0}/v{1}", UpdateOutPath, CodeVersion.CODE_VERSION);
            DirectoryInfo dicInfo = new DirectoryInfo(updateOutPath);
            if (!dicInfo.Exists) dicInfo.Create();

            EditorUtility.DisplayProgressBar("Copy Version AssetBundle File", "copy file to " + updateOutPath, 0.99f);

            string sourcePath, outfilePath;

            string verName = CUtils.GetRightFileName(Common.CRC32_VER_FILENAME);
            sourcePath = Path.Combine(CUtils.GetRealStreamingAssetsPath(), verName);
            outfilePath = Path.Combine(updateOutPath, verName);
            if (File.Exists(outfilePath)) File.Delete(outfilePath);
            EditorUtility.DisplayProgressBar("Copy Version AssetBundle ", "copy " + verName + " to " + outfilePath, 0.99f);
            File.Copy(sourcePath, outfilePath);
            Debug.LogFormat("Copy {0} to {1} sccuess!", sourcePath, outfilePath);

            EditorUtility.ClearProgressBar();
        }

        public static void CopyChangeFileToSplitFolder(bool firstExists, Dictionary<string, object[]> firstCrcDict, Dictionary<string, object[]> currCrcDict, Dictionary<string, object[]> diffCrcDict, HashSet<string> manualFileList)
        {
            Dictionary<string, object[]> updateList = new Dictionary<string, object[]>();

            object[] crc = null;

            if (firstExists)
            {
                foreach (var kv in diffCrcDict)
                {
                    updateList[kv.Key] = kv.Value;
                }

            }
            else
            {
               if (manualFileList.Count > 0)
                {
                    foreach (var abName in manualFileList)
                    {
                        if (currCrcDict.TryGetValue(abName, out crc))
                        {
                            updateList[abName] = crc;
                        }
                    }
                }

            }

            CopyFileToSplitFolder(updateList);

            AssetDatabase.Refresh();
        }


        public static void DeleteStreamingFiles(ICollection<string> abNames)
        {
            EditorUtility.DisplayProgressBar("Delete Streaming AssetBundle File", "", 0.09f);
            string path = CUtils.realStreamingAssetsPath;
            float all = abNames.Count;
            float i = 0;
            foreach (var file in abNames)
            {
                i = i + 1;
                string delPath = Path.Combine(path, file);
                File.Delete(delPath);
                File.Delete(delPath + ".meta");
                File.Delete(delPath + ".manifest");
                File.Delete(delPath + ".manifest.meta");
                EditorUtility.DisplayProgressBar("Delete Streaming AssetBundle File", "file " + file, i / all);

            }
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// split file copy to path
        /// </summary>
        public static string UpdateOutPath
        {
            get
            {
                if (string.IsNullOrEmpty(_updateOutPath))
                {
                    // _updateOutPath = Path.Combine(GetFirstOutPath(), CUtils.GetAssetPath("") + System.DateTime.Now.ToString("_yyyy-MM-dd_HH-mm"));
                    _updateOutPath = Path.Combine(GetFirstOutPath(), CUtils.platform);
                    DirectoryInfo dinfo = new DirectoryInfo(_updateOutPath);
                    if (!dinfo.Exists) dinfo.Create();
                }
                return _updateOutPath;
            }

            set
            {
                _updateOutPath = value;
            }
        }

        /// <summary>
        /// 版本输出目录
        /// </summary>
        public static string UpdateOutVersionPath
        {
            get{
                 string updateOutPath = string.Format("{0}/v{1}", UpdateOutPath, CodeVersion.CODE_VERSION);
                 return updateOutPath;
            }
        }

        #endregion


        #region private
        private static string _updateOutPath;
        public delegate StringBuilder filterSB(string key,StringBuilder manual, StringBuilder normal, HashSet<string> manualList);
        private static string GetFirstOutPath()
        {
            DirectoryInfo firstDir = new DirectoryInfo(Application.dataPath);
            string firstPath = Path.Combine(firstDir.Parent.FullName, Common.FirstOutPath);
            return firstPath;
        }

        private static void CopyFileToSplitFolder(Dictionary<string, object[]> updateList)
        {
            string updateOutPath = Path.Combine(UpdateOutPath, ResFolderName);//总的资源目录
            ExportResources.CheckDirectory(updateOutPath);
            string updateOutVersionPath =  Path.Combine(UpdateOutVersionPath,ResFolderName);//特定版本资源目录用于资源备份
            ExportResources.CheckDirectory(updateOutVersionPath);

            int allLen = updateList.Count;
            int i = 0;

            EditorUtility.DisplayProgressBar("Copy Change AssetBundle File", "copy file to " + updateOutPath, 0.09f);

            string sourcePath;
            string outfilePath,outfileVerionPath;
            string key,extension;
            uint crc = 0;
            StringBuilder erro = new StringBuilder();
            foreach (var k in updateList)
            {
                key = k.Key;//CUtils.GetAssetBundleName(k.Key);
                sourcePath = Path.Combine(Application.dataPath, k.Value[2].ToString());
                if(!File.Exists(sourcePath)) //
                {
                    string e = string.Format("copy file ({0}) not Exists ",sourcePath);
                    Debug.LogError(e);
                    erro.AppendLine(e);
                    continue;
                }
                extension = System.IO.Path.GetExtension(key);
                crc = (uint)k.Value[0];
                if (crc != 0)
                {
                    if(string.IsNullOrEmpty(extension))
                    {
                        key = key + "_" + crc.ToString() + Common.CHECK_ASSETBUNDLE_SUFFIX;
                    }else if(extension == Common.DOT_BYTES)
                    {
                        key = key.Replace(extension,Common.CHECK_ASSETBUNDLE_SUFFIX);
                        key = CUtils.InsertAssetBundleName(key, "_" + crc.ToString());// 
                    }else
                         key = CUtils.InsertAssetBundleName(key, "_" + crc.ToString());// 
                }
                outfilePath = Path.Combine(updateOutPath, key);
                outfileVerionPath = Path.Combine(updateOutVersionPath,key);

                FileHelper.CheckCreateFilePathDirectory(outfilePath);

                File.Copy(sourcePath, outfilePath, true);// source code copy

                if(HugulaEditorSetting.instance.backupRes)
                {
                    FileHelper.CheckCreateFilePathDirectory(outfileVerionPath);
                    File.Copy(sourcePath, outfileVerionPath, true);// source code copy
                }

                //check file crc
                uint filelen = 0;
                var copyFileCrc = CrcCheck.GetLocalFileCrc(outfilePath,out filelen);
                if(copyFileCrc!=crc)
                {
                    string e = string.Format("crc(source{0}!=copy{1}),path={2}",crc,copyFileCrc,outfilePath);
                    Debug.LogError(e);
                    erro.AppendLine(e);
                }
                EditorUtility.DisplayProgressBar("copy file to split folder " + updateOutPath, " copy file  =>" + i.ToString() + "/" + allLen.ToString(), i / allLen);
                i++;
            }
            Debug.Log(" copy  file complete!");
            EditorUtility.ClearProgressBar();
            string errContent = erro.ToString();
            if(!string.IsNullOrEmpty(errContent))
            {
                string tmpPath = BuildScript.GetAssetTmpPath();
                ExportResources.CheckDirectory(tmpPath);
                string outPath = Path.Combine(tmpPath, "error.txt");
                Debug.Log("write to path=" + outPath);
                using (StreamWriter sr = new StreamWriter(outPath, true))
                {
                    sr.WriteLine(" Error : "+System.DateTime.Now.ToString());
                    sr.Write(errContent);
                }
            }
        }

        #endregion
    }
}
