using System;
using Python.Runtime;

namespace PdfPlumber
{
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
