/*
 * MIT License
 *
 * Copyright (c) 2018-2021  Rotators
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:

 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ScanMsg
{
    public class FalloutMsg
    {
        public enum LoadStatus
        {
            OK,
            FileNameInvalid,
            FileDoesNotExists,
            FileIsEmpty,
            FileCannotRead,
            ExtraBracket,
            TextBeforeBracket,
            TextBetweenBracket,
            TextAfterBracket,
            InvalidFormat,
            InvalidId,
            DuplicatedId,
            TextTooLong
        }

        public class MsgEntry
        {
            public readonly uint Id;
            public string Sound;
            public List<string> Text = new List<string>();
            public uint Origin = 0;

            public int TextLen
            {
                get
                {
                    int result = 0;
                    foreach( string t in Text )
                    {
                        result += t.Length;
                    }

                    return result;
                }
            }

            public MsgEntry( uint id, string sound, params string[] text )
            {
                Id = id;
                Sound = sound;
                foreach( string t in text )
                {
                    Text.Add( t );
                }
            }

            public string AsString( int limit = 0 )
            {
                string text = "";
                if( limit > 0 )
                {
                    text = Text.First();

                    if( text.Length > limit )
                        text = text.Substring( 0, limit - 3 ) + "...";
                    else
                        text = text.Substring( 0, Math.Min( text.Length, limit ) );
                }
                else
                {
                    bool first = true;
                    foreach( string t in Text )
                    {
                        if( !first )
                            text += Environment.NewLine;

                        text += t;
                        first = false;
                    }
                }

                return $"{{{Id}}}{{{Sound}}}{{{text}}}";
            }
        }

        public readonly string Filename;
        public Dictionary<uint, MsgEntry> Msg = new Dictionary<uint, MsgEntry>();
        protected uint MsgLast = 0;

        private const int MaxTextLen = 1024;
        private const int MaxWordLen = 53;

        public FalloutMsg( string filename )
        {
            Filename = filename;
        }

        public LoadStatus Load( ref string report )
        {
            if( string.IsNullOrEmpty( Filename ) )
            {
                report = "invalid filename";

                return LoadStatus.FileNameInvalid;
            }
            else if( !File.Exists( Filename ) )
            {
                report = $"file does not exists [{Filename}]";

                return LoadStatus.FileDoesNotExists;
            }

            string[] fileLines;
            try
            {
                fileLines = File.ReadAllLines( Filename );
            }
            catch
            {
                report = $"file cannot be read [{Filename}]";

                return LoadStatus.FileCannotRead;
            }

            if( fileLines.Length == 0 )
            {
                report = $"file is empty [{Filename}]";

                return LoadStatus.FileIsEmpty;
            }

            uint lineNumber = 0;
            bool multi = false;

            foreach( string fileLine in fileLines )
            {
                lineNumber++;
                string line = fileLine.Replace( "\t", " " ).Replace( "\r", "" );

                LoadStatus status = LoadStatus.OK;
                string lineReport = "";
                if( !LoadLine( line, ref multi, ref status, ref lineReport ) )
                {
                    if( report.Length > 0 )
                        report += Environment.NewLine;

                    report += line + Environment.NewLine;
                    report += lineReport + $" [{Filename}:{lineNumber}]";
                }
            }

            return LoadStatus.OK;
        }

        protected bool LoadLine( string line, ref bool multi, ref LoadStatus status, ref string report )
        {
            Regex re = new Regex( "" );
            Match match = re.Match( "" );

            if( multi )
            {
                re = new Regex( "^(?<text>.*)(?<bracket>})(?<out>.*?)$" );
                match = re.Match( line );
                if( match.Success )
                {
                    multi = false;
                    string stripped = MergeGroups( re, match, "bracket" );
                    if( !CheckBrackets( stripped, ref report, true ) )
                    {
                        status = LoadStatus.ExtraBracket;

                        return false;
                    }

                    int len = Msg[MsgLast].TextLen + match.Groups["text"].Value.Length;
                    if( len > MaxTextLen )
                    {
                        report = new string( '-', MaxTextLen - Msg[MsgLast].TextLen ) + "^ text too long (multiline)";
                        status = LoadStatus.TextTooLong;

                        return false;
                    }

                    line = match.Groups["text"].Value;
                }

                if( line.Length > MaxWordLen )
                {
                    Regex wre = new Regex( "(\\w){" + MaxWordLen + ",}" );
                    Match wmatch = wre.Match( line );

                    if( wmatch.Success )
                    {
                        report = new string( '-', wmatch.Groups[0].Index + MaxWordLen ) + "^ word too long (multiline)";
                        status = LoadStatus.TextTooLong;

                        return false;
                    }
                }

                Msg[MsgLast].Text.Add( line );
            }
            else
            {
                bool processed = false;
                string stripped = "";

                // Yeah, yeah... I KNOW ;D
                string pattern =
                    "^" +
                    "(?<outFirst>.*?)" +
                    "(?<bracket1>{)" +
                    "(?<id>.*)" +
                    "(?<bracket2>})" +
                    "(?<out1>.*?)" +
                    "(?<bracket3>{)" +
                    "(?<sound>.*)" +
                    "(?<bracket4>})" +
                    "(?<out2>.*?)" +
                    "(?<bracket5>{)" +
                    "(?<text>.*)";

                // check for single line
                if( !processed )
                {
                    re = new Regex( pattern + "(?<bracketLast>})(?<outLast>.*?)$" );
                    match = re.Match( line );
                    processed = match.Success;
                }

                // check for multiline
                if( !processed )
                {
                    re = new Regex( pattern + "$" );
                    match = re.Match( line );
                    processed = multi = match.Success;
                }

                // check for empty lines and comments
                if( !processed )
                {
                    string trimLine = line.Trim( ' ', '\t', '\r' );
                    if( trimLine.Length == 0 )
                        return true;

                    if( trimLine.StartsWith( "#" ) ) // || trimLine.StartsWith(';') || trimLine.StartsWith("//"))
                        return true;
                }

                // give up
                if( !processed )
                {
                    status = LoadStatus.InvalidFormat;
                    report = "^ invalid format";
                    return false;
                }

                stripped = MergeGroups( re, match, "bracket" );
                if( !CheckBrackets( stripped, ref report ) )
                {
                    status = LoadStatus.ExtraBracket;

                    return false;
                }

                foreach( string group in new string[] { "outFirst", "out1", "out2", "outLast" } )
                {
                    if( match.Groups[group].Value.Length > 0 )
                    {
                        bool error = true, outFirstOrLast = (group == "outFirst" || group == "outLast");
                        string trimLine = match.Groups[group].Value.Trim( ' ', '\t', '\r' );

                        // skip text before/after brackets if it starts with '#'
                        // should report error imho, but good luck telling that to decades of hand-edited files :)
                        if( outFirstOrLast && trimLine.StartsWith( "#" ) )
                            error = false;
                        // skip blanks before/between/after brackets
                        else if( trimLine.Length == 0 )
                            error = false;

                        // in relaxed mode, skip text before/after brackets if it starts with ';' or '//'
                        if( error && ScanMsg.Options.Relaxed && outFirstOrLast && (trimLine.StartsWith( ";" ) || trimLine.StartsWith( "//" )) )
                            error = false;

                        if( error )
                        {
                            string where = null;

                            switch( group )
                            {
                                case "outFirst":
                                    where = "before";
                                    status = LoadStatus.TextBeforeBracket;
                                    break;
                                case "outLast":
                                    where = "after";
                                    status = LoadStatus.TextAfterBracket;
                                    break;
                                default:
                                    where = "between";
                                    status = LoadStatus.TextBetweenBracket;
                                    break;
                            }
                            report = new string( '-', match.Groups[group].Index ) + $"^ text {where} brackets";

                            return false;
                        }
                    }
                }

                if( match.Groups["text"].Value.Length > MaxTextLen )
                {
                    report = new string( '-', match.Groups["text"].Index + MaxTextLen ) + "^ text too long";
                    status = LoadStatus.TextTooLong;

                    return false;
                }

                if( match.Groups["text"].Value.Length > MaxWordLen )
                {
                    Regex wre = new Regex( "(\\w){" + MaxWordLen + ",}" );
                    Match wmatch = wre.Match( match.Groups["text"].Value );

                    if( wmatch.Success )
                    {
                        report = new string( '-', match.Groups["text"].Index + wmatch.Groups[0].Index + MaxWordLen ) + "^ word too long";
                        status = LoadStatus.TextTooLong;

                        return false;
                    }
                }

                // always last
                uint id = CheckId( match.Groups["id"], ref report );
                if( id == uint.MaxValue )
                {
                    status = LoadStatus.InvalidId;

                    return false;
                }

                if( Msg.ContainsKey( id ) )
                {
                    report = new string( '-', match.Groups["id"].Index ) + "^ duplicated id";
                    report += $" (previous: {Msg[id].AsString( 15 )})";
                    status = LoadStatus.DuplicatedId;

                    return false;
                }

                Msg[id] = new MsgEntry( id, match.Groups["sound"].Value, match.Groups["text"].Value );
                MsgLast = id;
            }

            return true;
        }

        protected string MergeGroups( Regex re, Match match, string groupsToSpace )
        {
            string result = "";

            for( int g = 1; g < match.Groups.Count; g++ )
            {
                if( re.GroupNameFromNumber( g ).StartsWith( groupsToSpace ) )
                {
                    result += " ";

                    continue;
                }
                result += match.Groups[g].Value;
            }

            return result;
        }

        protected bool CheckBrackets( string stripped, ref string report, bool multi = false )
        {
            Match brackets = Regex.Match( stripped, "({|})" );
            if( brackets.Success )
            {
                if( brackets.Groups[1].Index > 0 )
                    report = new string( '-', brackets.Groups[1].Index ) + "^";
                else
                    report = "^";

                report += " bracket not allowed here";
                if( multi )
                    report += " (multiline)";

                return false;
            }

            return true;
        }

        protected uint CheckId( Group group, ref string report )
        {
            uint id = uint.MaxValue;

            if( group.Value.Length == 0 ||
                !Regex.Match( group.Value, "^[0-9]+$" ).Success ||
                !uint.TryParse( group.Value, out id ) )
            {
                report = new string( '-', group.Index ) + "^ invalid id";

                return uint.MaxValue;
            }

            return id;
        }
    }

    public class ScanMsg
    {
        public static class Options
        {
            public static bool NoExitcode = false;
            public static bool Relaxed = false;
        }

        private static string ReportFile = "ScanMsg.log";
        private static int ReportCount = 0;

        private static int Main( string[] args )
        {
            if( File.Exists( ReportFile ) )
            {
                try
                {
                    File.Delete( ReportFile );
                }
                catch
                {
                    Console.WriteLine( $"WARNING: cannot delete previous created {ReportFile}" );
                    ReportFile = "";
                }
            }

            if( args.Length > 0 && args[0] == "--language-base" )
                MainLang( args.ToList() );
            else
                MainScan( args.ToList() );

            if( Options.NoExitcode )
                return 0;

            return ReportCount;

        }

        private static void MainLang( List<string> args )
        {
            string usage = "USAGE: ScanMsg --language-base [path/to/text/language] --translations [path/to/text/translation1] <path/to/text/translation2> ...";

            if( args.Count < 4 )
            {
                Report( usage );
                return;
            }
            else if( args[0] != "--language-base" || args[2] != "--translations" )
            {
                Report( usage );
                return;
            }

            args.RemoveAt( 0 ); // --language-base
            args.RemoveAt( 1 ); // --translations

            for( int d = 0, dLen = args.Count; d < dLen; d++ )
            {
                if( !Directory.Exists( args[d] ) )
                {
                    Report( $"ERROR: Invalid directory {args[d]}" );
                    return;
                }
                args[d] = args[d].Replace( '\\', '/' ).Replace( '/', Path.DirectorySeparatorChar );
            }

            Console.Write( "Scanning files..." );

            string baseDir = args[0];
            args.RemoveAt( 0 ); // base language

            List<string> baseFiles = Directory.GetFiles( baseDir, "*.*", SearchOption.AllDirectories ).Where( file => file.ToLower().EndsWith( ".msg" ) ).OrderBy( f => f ).Select( file => { return file.Substring( baseDir.Length ).TrimStart( '/', '\\' ); } ).ToList();
            Console.WriteLine( $" {baseFiles.Count} found" );

            Dictionary<string, FalloutMsg> baseLang = new Dictionary<string, FalloutMsg>();

            foreach( string langDir in args )
            {
                foreach( string baseFile in baseFiles )
                {
                    string file = Path.Combine( langDir, baseFile );
                    if( file.StartsWith( ".\\" ) || file.StartsWith( "./" ) )
                        file = file.Substring( 2 );

                    if( !File.Exists( file ) )
                    {
                        Report( $"file does not exists [{file}]" );
                        continue;
                    }

                    string report = "";

                    FalloutMsg baseMsg, langMsg;
                    FalloutMsg.LoadStatus status;

                    if( baseLang.ContainsKey( baseFile ) )
                        baseMsg = baseLang[baseFile];
                    else
                    {
                        baseMsg = new FalloutMsg( Path.Combine( baseDir, baseFile ) );
                        status = baseMsg.Load( ref report );

                        if( report.Length > 0 )
                            Report( report );
                        else if( status != FalloutMsg.LoadStatus.OK )
                            Report( $"WARNING: missing report for error<{status.ToString()}> [{file}]" );

                        // cannot continue if base language .msg contains errors
                        if( report.Length > 0 || status != FalloutMsg.LoadStatus.OK )
                            continue;

                        baseLang[baseFile] = baseMsg;
                        report = "";
                    }

                    langMsg = new FalloutMsg( file );
                    status = langMsg.Load( ref report );

                    if( report.Length > 0 )
                        Report( report );
                    else if( status != FalloutMsg.LoadStatus.OK )
                        Report( $"WARNING: missing report for error<{status.ToString()}> [{file}]" );

                    // cannot continue if translation .msg contains errors
                    if( report.Length > 0 || status != FalloutMsg.LoadStatus.OK )
                        continue;

                    //

                    foreach( KeyValuePair<uint, FalloutMsg.MsgEntry> kvp in baseMsg.Msg )
                    {
                        if( !langMsg.Msg.ContainsKey( kvp.Key ) )
                        {
                            Report( $"message id {{{kvp.Key}}} missing [{file}]" );
                            continue;
                        }

                        string textBase = string.Join( " ", kvp.Value.Text.ToArray() );
                        string textLang = string.Join( " ", langMsg.Msg[kvp.Key].Text.ToArray() );

                        if( string.IsNullOrEmpty( textBase ) && !string.IsNullOrEmpty( textLang ) )
                        {
                            Report( $"message id {{{kvp.Key}}} should be empty [{file}]" );
                            // Report( $"base  {textBase}" );
                            // Report( $"lang  {textLang}" );
                        }
                        else if( !string.IsNullOrEmpty( textBase ) && string.IsNullOrEmpty( textLang ) )
                        {
                            Report( $"message id {{{kvp.Key}}} should not be empty [{file}]" );
                            // Report( $"base  {textBase}" );
                            // Report( $"lang  {textLang}" );
                        }
                    }
                }
            }
        }

        private static void MainScan( List<string> args )
        {
            // extract options leaving only files/directories in place
            for( int a = 0, aLen = args.Count; a < aLen; a++ )
            {
                string arg = args[a];
                if( arg.StartsWith( "--" ) )
                {
                    switch( arg.Substring( 2 ) )
                    {
                        case "no-exitcode":
                            Options.NoExitcode = true;
                            break;
                        case "relaxed":
                            Options.Relaxed = true;
                            break;
                        default:
                            Report( $"Unknown option '{arg}'" );
                            return;
                    }
                    args.RemoveAt( a-- );
                    aLen--;
                }
            }

            // if started without arguments, check current directory
            if( args.Count == 0 )
                args.Add( "." );

            Console.Write( "Scanning files..." );

            List<string> files = new List<string>();
            foreach( string arg in args )
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes( arg );
                }
                catch
                {
                    Report( $"ERROR: Cannot access '{arg}' attributes" );
                    return;
                }

                // TODO (Linux): `ScanMsg /root` results in crash

                if( (attributes & FileAttributes.Directory) == FileAttributes.Directory )
                    files.AddRange( Directory.GetFiles( arg, "*.*", SearchOption.AllDirectories ).Where( file => file.ToLower().EndsWith( ".msg" ) ).OrderBy( f => f ) );
                else if( File.Exists( arg ) )
                    files.Add( arg );
                else
                {
                    Report( $"ERROR: Invalid target '{arg}'" );
                    return;
                }
            }
            Console.WriteLine( $" {files.Count} found" );

            foreach( string fname in files )
            {
                string report = "";
                string file = fname.Replace( '\\', '/' ).Replace( '/', Path.DirectorySeparatorChar );
                if( file.StartsWith( ".\\" ) || file.StartsWith( "./" ) )
                    file = file.Substring( 2 );

                FalloutMsg msg = new FalloutMsg( file );
                FalloutMsg.LoadStatus status = msg.Load( ref report );

                if( report.Length > 0 )
                    Report( report );
                else if( status != FalloutMsg.LoadStatus.OK )
                    Report( $"WARNING: missing report for error<{status.ToString()}> [{file}]" );
            }
        }

        private static void Report( string report )
        {
            if( Debugger.IsAttached && Debugger.IsLogging() )
                Debugger.Log( 0, null, report + Environment.NewLine );

            Console.WriteLine( report );
            ReportCount++;

            if( string.IsNullOrEmpty( ReportFile ) )
                return;

            try
            {
                File.AppendAllText( ReportFile, report + Environment.NewLine );
            }
            catch
            {
                Console.WriteLine( $"WARNING: cannot update {ReportFile}" );
                ReportFile = "";
            }
        }
    }
}
