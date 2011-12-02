//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Nick Malaguti">
//   Copyright (c) Nick Malaguti.
// </copyright>
// <license>
//   This source code is subject to the MIT License
//   See http://www.opensource.org/licenses/mit-license.html
//   All other rights reserved.
// </license>
//-----------------------------------------------------------------------

namespace PythonScripting
{
    using System;
    using System.IO;
    using IronPython.Hosting;
    using Microsoft.Scripting.Hosting;
    using Newtonsoft.Json.Linq;
    using TurntableBotSharp;

    /// <summary>
    /// Example program that will invoke a python script 
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Name of the python script
        /// </summary>
        private const string Script = "script.py";

        /// <summary>
        /// Script Scope
        /// </summary>
        private static ScriptScope scope;

        /// <summary>
        /// Script Engine
        /// </summary>
        private static ScriptEngine engine;

        /// <summary>
        /// Main method
        /// </summary>
        /// <param name="args">Command line args.</param>
        public static void Main(string[] args)
        {
            engine = Python.CreateEngine();
            scope = engine.CreateScope();

            engine.Runtime.LoadAssembly(typeof(TurntableBot).Assembly);
            engine.Runtime.LoadAssembly(typeof(JObject).Assembly);
            engine.Runtime.IO.RedirectToConsole();

            ScriptSource source = null;

            if (File.Exists(Script))
            {
                source = engine.CreateScriptSourceFromFile(Script);
            }
            else
            {
                Console.WriteLine("File not found");
            }

            if (source != null)
            {
                try
                {
                    source.Execute(scope);
                }
                catch (Exception e)
                {
                    ExceptionOperations ops = engine.GetService<ExceptionOperations>();
                    Console.WriteLine(ops.FormatException(e));
                }
            }

            Console.ReadLine();
        }
    }
}