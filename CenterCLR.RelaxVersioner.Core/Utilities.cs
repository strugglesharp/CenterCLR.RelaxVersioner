﻿/////////////////////////////////////////////////////////////////////////////////////////////////
//
// RelaxVersioner - Easy-usage, Git-based, auto-generate version informations toolset.
// Copyright (c) 2016-2021 Kouji Matsui (@kozy_kekyo, @kekyo2)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//	http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
/////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using LibGit2Sharp;

using RelaxVersioner.Writers;

namespace RelaxVersioner
{
    internal static class Utilities
    {
        private static readonly char[] directorySeparatorChar_ =
            { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        public static Dictionary<string, WriterBase> GetWriters()
        {
            return typeof(Utilities).Assembly.
                GetTypes().
                Where(type => type.IsSealed && type.IsClass && typeof(WriterBase).IsAssignableFrom(type)).
                Select(type => (WriterBase)Activator.CreateInstance(type)).
                ToDictionary(writer => writer.Language, StringComparer.InvariantCultureIgnoreCase);
        }

        private static T TraversePathToRoot<T>(string candidatePath, Func<string, T> action)
            where T : class
        {
            var path = Path.GetFullPath(candidatePath).
                TrimEnd(directorySeparatorChar_);

            while (true)
            {
                var result = action(path);
                if (result != null)
                {
                    return result;
                }

                var index = path.LastIndexOfAny(directorySeparatorChar_);
                if (index == -1)
                {
                    return null;
                }

                path = path.Substring(0, index);
            }
        }

        public static string GetDirectoryNameWithoutTrailingSeparator(string path) =>
            path.TrimEnd(directorySeparatorChar_);

        public static string GetDirectoryNameWithTrailingSeparator(string path) =>
            GetDirectoryNameWithoutTrailingSeparator(path) + Path.DirectorySeparatorChar;

        public static Repository OpenRepository(Logger logger, string candidatePath)
        {
            var repository = TraversePathToRoot(candidatePath, path =>
            {
                if (Directory.Exists(Path.Combine(path, ".git")))
                {
                    string GetNativeLibraryPath()
                    {
                        try
                        {
                            return GlobalSettings.NativeLibraryPath ?? "(null)";
                        }
                        catch
                        {
                            return "(unspecified)";
                        }
                    }

                    logger.Message(LogImportance.Low, "libgit2sharp.NativeLibraryPath, Path={0}", GetNativeLibraryPath());

                    try
                    {
                        var r = new Repository(GetDirectoryNameWithTrailingSeparator(path));
                        logger.Message(LogImportance.Low, "Repository opened, Path={0}", path);
                        return r;
                    }
                    catch (RepositoryNotFoundException ex)
                    {
                        logger.Message(LogImportance.Low, ex, "Cannot open repository, Path={0}", path);
                    }
                }
                else
                {
                    logger.Message(LogImportance.Low, "This directory doesn't contain repository, Path={0}", path);
                }

                return null;
            });

            if (repository == null)
            {
                logger.Warning("Repository not found, CandidatePath={0}", candidatePath);
            }

            return repository;
        }

        public static TValue GetValue<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue)
        {
            Debug.Assert(dictionary != null);
            Debug.Assert(key != null);

            if (dictionary.TryGetValue(key, out TValue value) == false)
            {
                value = defaultValue;
            }

            return value;
        }

        public static Version GetSafeVersionFromDate(Commit commit)
        {
            if(commit==null)
            {
                return new Version(0, 0, 0, 9999);
            }
            var date = commit.Author.When;
            var gitCommitCounter = LookupGitTotalCommitCount(commit);
            return new Version((date.Year-2000)*1000+ gitCommitCounter,Int32.Parse(date.ToString("MMdd")), Int32.Parse(date.ToString("HHmm")));
        }
        private static int LookupGitTotalCommitCount(Commit commit, Dictionary<Commit, int> rstDict = null)
        {
            if (commit == null)
                return 9999;
            if (rstDict == null)
            {
                rstDict = new Dictionary<Commit, int>();
            }
         //   Console.WriteLine(commit.Committer.When + ":" + commit.Id + ":" + commit.Message);

            if (rstDict.TryGetValue(commit, out int rst))
            {
                return rst;
            }

            if (commit.Parents == null || commit.Parents.Count() == 0)
            {
                rstDict.Add(commit, 1);
                return 1;
            }
            else
            {
                var allParetn = commit.Parents.Select(parentCommit => LookupGitTotalCommitCount(parentCommit, rstDict)).ToList();
                var maxParetn = allParetn.Max();
                var currRst = 1 + maxParetn;
                rstDict.Add(commit, currRst);
                return currRst;
            }
        }

        public static IEnumerable<XElement> LoadRuleSets(string candidatePath)
        {
            Debug.Assert(candidatePath != null);

            var path = Path.GetFullPath(candidatePath).
                TrimEnd(directorySeparatorChar_);

            while (true)
            {
                var rulePath = Path.Combine(path, "RelaxVersioner.rules");
                if (File.Exists(rulePath))
                {
                    XElement element = null;
                    try
                    {
                        element = XElement.Load(rulePath);
                    }
                    catch
                    {
                    }

                    if (element != null)
                    {
                        yield return element;
                    }
                }

                var index = path.LastIndexOfAny(directorySeparatorChar_);
                if (index == -1)
                {
                    break;
                }

                path = path.Substring(0, index);
            }
        }

        public static Dictionary<string, XElement> GetElementSets(IEnumerable<XElement> ruleSets)
        {
            Debug.Assert(ruleSets != null);

            return
                (from ruleSet in ruleSets
                 where (ruleSet != null) && (ruleSet.Name.LocalName == "RelaxVersioner")
                 from rules in ruleSet.Elements("WriterRules")
                 from language in rules.Elements("Language")
                 where !string.IsNullOrWhiteSpace(language?.Value)
                 select new { language, rules }).
                GroupBy(
                    entry => entry.language.Value.Trim(),
                    entry => entry.rules,
                    StringComparer.InvariantCultureIgnoreCase).
                ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.InvariantCultureIgnoreCase);
        }

        public static IEnumerable<string> AggregateImports(XElement wrules)
        {
            return (from import in wrules.Elements("Import")
                    select import.Value.Trim());
        }

        public static IEnumerable<Rule> AggregateRules(XElement wrules)
        {
            return (from rule in wrules.Elements("Rule")
                    let name = rule.Attribute("name")
                    let key = rule.Attribute("key")
                    where !string.IsNullOrWhiteSpace(name?.Value)
                    select new Rule(name.Value.Trim(), key?.Value.Trim(), rule.Value.Trim()));
        }

        public static XElement GetDefaultRuleSet()
        {
            var type = typeof(Utilities);
            using (var stream = type.Assembly.GetManifestResourceStream(
                type, "DefaultRuleSet.rules"))
            {
                return XElement.Load(stream);
            }
        }

        public static string GetFriendlyName<TObject>(this ReferenceWrapper<TObject> refer)
            where TObject : GitObject =>
            (refer.CanonicalName == "(no branch)") ?
                "HEAD" :
                string.IsNullOrWhiteSpace(refer.FriendlyName) ? refer.CanonicalName : refer.FriendlyName;

        public static Version IncrementLastVersionComponent(Version version, int value)
        {
            if (version.Revision.HasValue)
            {
                return new Version(
                    version.Major,
                    version.Minor.Value,
                    version.Build.Value,
                    version.Revision.Value + value);
            }
            else if (version.Build.HasValue)
            {
                return new Version(
                    version.Major,
                    version.Minor.Value,
                    version.Build.Value + value);
            }
            else if (version.Minor.HasValue)
            {
                return new Version(
                    version.Major,
                    version.Minor.Value + value);
            }
            else
            {
                return new Version(
                    version.Major + value);
            }
        }
    }
}
