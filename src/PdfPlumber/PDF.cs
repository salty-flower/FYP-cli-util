using System;
using Python.Runtime;

namespace DataCollection.PdfPlumber;

public class PDF : Container, IDisposable
{
    private dynamic _pyPdf;
    private bool _disposed;

    public Page[] Pages { get; private set; }

    private PDF(dynamic pyPdf)
    {
        _pyPdf = pyPdf;
        InitializePages();
    }

    private void InitializePages()
    {
        using (Py.GIL())
        {
            var pyPages = _pyPdf.pages;
            Pages = new Page[pyPages.__len__()];

            for (int i = 0; i < Pages.Length; i++)
            {
                Pages[i] = new Page(pyPages[i]);
            }
        }
    }

    public static PDF open(string path)
    {
        PythonEngine.Initialize();
        using (Py.GIL())
        {
            dynamic pdfplumber = Py.Import("pdfplumber");
            var pyPdf = pdfplumber.open(path);
            return new PDF(pyPdf);
        }
    }

    public void close()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            using (Py.GIL())
            {
                _pyPdf.close();
                _pyPdf = null;
            }
            _disposed = true;
        }
    }
}
