using System;
using System.Collections.Generic;

public class Heap<T>
{
    protected List<T> Data = new();
    protected Func<T, T, bool> Compare;
    protected Func<T, T, bool> Equal;

    /// <summary>
    /// Creates a new heap using compare to order
    /// </summary>
    /// <param name="compare">compare(t1, t2) should return true if t1 should be before t2</param>
    public Heap(Func<T, T, bool> compare, Func<T, T, bool> equal)
    {
        Compare = compare;
        Equal = equal;
    }

    public int Count()
    {
        return Data.Count;
    }

    /// <summary>
    /// Clears the heap of data without resizing
    /// </summary>
    public void Clear()
    {
        Data.Clear();
    }

    /// <summary>
    /// Adds a new element to the heap
    /// </summary>
    public void Push(T data)
    {
        Data.Add(data);
        HeapifyUp(Data.Count - 1);
    }

    /// <summary>
    /// Finds and replaces the oldData with the newData
    /// </summary>
    /// <param name="oldData"></param>
    /// <param name="newData"></param>
    public void Replace(T oldData, T newData)
    {
        int i = 0;
        while (true)
        {
            if (Equal(Data[i], oldData))
            {
                Data[i] = newData;
                if(Compare(oldData, newData))
                    HeapifyUp(i);
                else
                    HeapifyDown(i);
                return;
            }
            int left = (i << 1) + 1;
            int right = left + 1;
            int smallest = i;

            if (left < Data.Count && Compare(Data[left], Data[smallest]))
                smallest = left;
            if (right < Data.Count && Compare(Data[right], Data[smallest]))
                smallest = right;
            if (smallest == i) break;

            (Data[i], Data[smallest]) = (Data[smallest], Data[i]);
            i = smallest;
        }
    }

    /// <summary>
    /// Removes and returns the first element
    /// </summary>
    public T Pop()
    {
        var result = Data[0];
        int lastIdx = Data.Count - 1;
        Data[0] = Data[lastIdx];
        Data.RemoveAt(lastIdx);
        if (Data.Count > 0)
            HeapifyDown(0);
        return result;
    }

    /// <summary>
    /// Returns the first element without removal
    /// </summary>
    /// <returns></returns>
    public T Peak()
    {
        if(Data.Count > 0)
            return Data[0];
        return default;
    }

    /// <summary>
    /// Bubbles node up until heap property holds
    /// </summary>
    protected void HeapifyUp(int i)
    {
        while (i > 0)
        {
            int parentIdx = (i - 1) >> 1;
            if (!Compare(Data[i], Data[parentIdx])) break;
            (Data[i], Data[parentIdx]) = (Data[parentIdx], Data[i]);
            i = parentIdx;
        }
    }

    /// <summary>
    /// Pushes node down until heap property restored
    /// </summary>
    protected void HeapifyDown(int i)
    {
        while (true)
        {
            int left = (i << 1) + 1;
            int right = left + 1;
            int smallest = i;

            if (left < Data.Count && Compare(Data[left], Data[smallest]))
                smallest = left;
            if (right < Data.Count && Compare(Data[right], Data[smallest]))
                smallest = right;
            if (smallest == i) break;

            (Data[i], Data[smallest]) = (Data[smallest], Data[i]);
            i = smallest;
        }
    }
}