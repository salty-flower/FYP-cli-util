using System;
using System.Collections.Generic;
using System.IO;
using DataCollection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Python.Runtime;

namespace DataCollection.Services;

/// <summary>
/// Service for natural language processing operations using Python's NLTK
/// </summary>
public class NlpService(ILogger<NlpService> logger, IOptions<PathsOptions> pathsOptions)
{
    private readonly PathsOptions _pathsOptions = pathsOptions.Value;
    private bool _isInitialized = false;
    private string _nlpUtilsScript;

    /// <summary>
    /// Initialize the Python runtime and required NLTK components
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
            return;

        try
        {
            logger.LogInformation("Initializing Python runtime for NLP processing");

            // Initialize Python runtime
            Runtime.PythonDLL = _pathsOptions.PythonDLL;
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            // Load the Python script content
            string scriptPath = "nlp_utils.py";
            _nlpUtilsScript = File.ReadAllText(scriptPath);
            logger.LogInformation("Loaded NLP utilities script from {ScriptPath}", scriptPath);

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing Python environment for NLP");
            throw;
        }
    }

    /// <summary>
    /// Analyze adjectives in a sentence with counts
    /// </summary>
    /// <param name="text">The text to analyze</param>
    /// <returns>Dictionary with adjective frequency data</returns>
    public Dictionary<string, int> AnalyzeSentenceAdjectives(string text)
    {
        Initialize();

        using (Py.GIL())
        {
            try
            {
                // Create globals dictionary for Python execution
                using var globals = new PyDict();

                // Execute the NLP utils script in the global context
                PythonEngine.Exec(_nlpUtilsScript, globals);

                // Create function call script
                string functionCall =
                    $"analyze_sentence_adjectives('''{text.Replace("'", "\\'")}''')";

                // Execute the function call
                dynamic result = PythonEngine.Eval(functionCall, globals);

                // Access the frequency dictionary
                PyDict frequency = result["frequency"].As<PyDict>();

                // Convert Python dict to C# dictionary
                var adjectiveCounts = new Dictionary<string, int>();

                // Iterate through the dictionary items
                foreach (PyObject key in frequency.Keys())
                {
                    string adjective = key.ToString();
                    int count = Convert.ToInt32(frequency[key].ToString());
                    adjectiveCounts[adjective] = count;
                }

                return adjectiveCounts;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error analyzing sentence adjectives: {ErrorMessage}",
                    ex.Message
                );
                return new Dictionary<string, int>();
            }
        }
    }
}
