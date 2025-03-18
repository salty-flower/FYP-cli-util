using System;
using System.Diagnostics.CodeAnalysis;
using DataCollection.Models;
using Python.Runtime;

namespace PdfPlumber
{
    [RequiresUnreferencedCode("Wrapper of Python")]
    public class Page : Container
    {
        private readonly dynamic _pyPage;

        internal Page(dynamic pyPage)
        {
            _pyPage = pyPage;
            Width = (float)_pyPage.width;
            Height = (float)_pyPage.height;
        }

        public float Width { get; set; }
        public float Height { get; set; }

        public Page crop(float x0, float y0, float x1, float y1)
        {
            using (Py.GIL())
            {
                var croppedPyPage = _pyPage.crop(x0, y0, x1, y1);
                return new CroppedPage(croppedPyPage);
            }
        }

        public Page filter(string predicate)
        {
            using (Py.GIL())
            {
                var filteredPyPage = _pyPage.filter(predicate);
                return new FilteredPage(filteredPyPage);
            }
        }

        public MatchObject[] extractTextLines()
        {
            using (Py.GIL())
            {
                var pyDictList = _pyPage.extract_text_lines();
                var mos = new MatchObject[pyDictList.__len__()];

                for (int i = 0; i < mos.Length; i++)
                    mos[i] = new MatchObject(
                        pyDictList[i]["text"].As<string>(),
                        pyDictList[i]["x0"].As<float>(),
                        pyDictList[i]["top"].As<float>(),
                        pyDictList[i]["x1"].As<float>(),
                        pyDictList[i]["bottom"].As<float>()
                    );
                ;

                return mos;
            }
        }

        public Table[] extractTables()
        {
            using (Py.GIL())
            {
                var pyTables = _pyPage.extract_tables();
                var tables = new Table[pyTables.__len__()];

                for (int i = 0; i < tables.Length; i++)
                {
                    tables[i] = new Table(pyTables[i]);
                }

                return tables;
            }
        }

        public object[] extractWords()
        {
            using (Py.GIL())
            {
                var pyWords = _pyPage.extract_words();
                // Convert Python list of dicts to C# array
                var words = new object[pyWords.__len__()];
                for (int i = 0; i < words.Length; i++)
                {
                    words[i] = pyWords[i].As<object>();
                }
                return words;
            }
        }

        public string extractText()
        {
            using (Py.GIL())
            {
                return _pyPage.extract_text().As<string>();
            }
        }
    }

    public class DerivedPage : Page
    {
        internal DerivedPage(DerivedPage pyPage)
            : base(pyPage) { }
    }

    public class CroppedPage : DerivedPage
    {
        internal CroppedPage(CroppedPage pyPage)
            : base(pyPage) { }
    }

    public class FilteredPage : DerivedPage
    {
        internal FilteredPage(FilteredPage pyPage)
            : base(pyPage) { }
    }

    public class PDFPageAggregatorWithMarkedContent
    {
        public void aggregate()
        {
            throw new NotImplementedException();
        }
    }
}
