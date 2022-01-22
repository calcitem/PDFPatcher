﻿using System.Collections;
using System.Collections.Generic;
using PDFPatcher.Common;

namespace PDFPatcher.Model;

internal struct PageRange : IEnumerable<int>
{
    public int StartValue, EndValue;

    public PageRange(int startValue, int endValue)
    {
        StartValue = startValue;
        EndValue = endValue;
    }

    public bool Contains(int value) =>
        value >= StartValue && value <= EndValue
        || value >= EndValue && value <= StartValue;

    public override string ToString() =>
        StartValue != EndValue
            ? string.Concat(StartValue.ToText(), '-', EndValue.ToText())
            : StartValue.ToText();

    /// <summary>
    ///     返回范围中包含的数量。
    /// </summary>
    public int Count => (EndValue > StartValue ? EndValue - StartValue : StartValue - EndValue) + 1;

    #region IEnumerable<int> 成员

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => new PageRangeEnumerator(StartValue, EndValue);

    #endregion

    #region IEnumerable 成员

    IEnumerator IEnumerable.GetEnumerator() => new PageRangeEnumerator(StartValue, EndValue);

    #endregion

    private sealed class PageRangeEnumerator : IEnumerator<int>
    {
        private readonly bool _isIncremental;
        private readonly int _start, _end;

        public PageRangeEnumerator(int start, int end)
        {
            _start = start;
            _isIncremental = start < end;
            _end = end;
            Current = _isIncremental ? start - 1 : start + 1;
        }

        #region IEnumerator<int> 成员

        public int Current { get; private set; }

        #endregion

        #region IDisposable 成员

        public void Dispose()
        {
        }

        #endregion

        #region IEnumerator 成员

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_isIncremental && Current < _end)
            {
                Current++;
                return true;
            }

            if (_isIncremental || Current <= _end)
            {
                return false;
            }

            Current--;
            return true;
        }

        public void Reset() => Current = _start < _end ? _start : _end;

        #endregion
    }
}
