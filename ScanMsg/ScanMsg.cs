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
            ExtraBracket,
            TextBeforeBracket,
            TextBetweenBracket,
            TextAfterBracket,
            InvalidFormat,
            InvalidId,
            DuplicatedId,
            TextTooLong,
        }

        public class MsgEntry
        {
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

            public MsgEntry( string sound, params string[] text )
            {
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

                return "{" + Sound + "}{" + text + "}";
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

            string[] fileLines = File.ReadAllLines( Filename );
            if( fileLines.Length == 0 )
            {
                report = $"file is empty [{Filename}]";

                return LoadStatus.FileIsEmpty;
            }

            uint number = 0;
            bool multi = false;

            foreach( string fileLine in fileLines )
            {
                number++;
                string line = fileLine.Replace( "\t", " " ).Replace( "\r", "" );

                LoadStatus status = LoadStatus.OK;
                string lineReport = "";
                if( !LoadLine( line, ref multi, ref status, ref lineReport ) )
                {
                    if( report.Length > 0 )
                        report += Environment.NewLine;

                    report += line + Environment.NewLine;
                    report += lineReport + $" [{Filename}:{number}]";
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
                        bool error = true;

                        // skip text before/after brackets if it starts with '#'
                        // should report error imho, but good luck telling that to decades of hand-edited files :)
                        if( (group == "outFirst" || group == "outLast") &&
                            match.Groups[group].Value.Trim( ' ', '\t', '\r' ).StartsWith( "#" ) )
                            error = false;
                        // skip blanks before/between/after brackets
                        else if( match.Groups[group].Value.Trim( ' ', '\t', '\r' ).Length == 0 )
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

                if( id > 0 && Msg.ContainsKey( id ) )
                {
                    report = new string( '-', match.Groups["id"].Index ) + "^ duplicated id";
                    report += " (previous: {" + id + "}" + Msg[id].AsString( 15 ) + ")";
                    status = LoadStatus.DuplicatedId;

                    return false;
                }

                Msg[id] = new MsgEntry( match.Groups["sound"].Value, match.Groups["text"].Value );
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

        public string AsString()
        {
            string result = "";

            foreach( KeyValuePair<uint, MsgEntry> kv in Msg.OrderBy( d => d.Key ) )
            {
                result += "{" + kv.Key + "}" + kv.Value.AsString();
                result += Environment.NewLine;
            }

            return result;
        }
    }

    public class ScanMsg
    {
        private static string ReportFile = "ScanMsg.log";

        private static void Main( string[] args )
        {
            Console.Write( "Scanning files..." );

            if( File.Exists( ReportFile ) )
                File.Delete( ReportFile );

            string dir = ".";
            if( args.Length >= 1 )
            {
                dir = args[0];
                if( !Directory.Exists( dir ) )
                {
                    Console.WriteLine( "Invalid directory" );
                    Environment.Exit( 1 );
                }
            }

            string[] files = Directory.GetFiles( dir, "*.msg", SearchOption.AllDirectories ).OrderBy( f => f ).ToArray();
            Console.WriteLine( $" {files.Length} found" );

            foreach( string fname in files )
            {
                string report = "";
                string file = fname.TrimStart( '.', '/', '\\' );

                FalloutMsg msg = new FalloutMsg( file );
                FalloutMsg.LoadStatus status = msg.Load( ref report );

                if( report.Length > 0 )
                    Report( report );
                else if( status != FalloutMsg.LoadStatus.OK )
                    Report( $"missing report for error<{status.ToString()}> [{file}]" );
            }
        }

        private static void Report( string report = "" )
        {
            if( Debugger.IsAttached && Debugger.IsLogging() )
                Debugger.Log( 0, null, report + Environment.NewLine );

            Console.WriteLine( report );
            if( !string.IsNullOrEmpty( ReportFile ) )
                File.AppendAllText( ReportFile, report + Environment.NewLine );
        }
    }
}
