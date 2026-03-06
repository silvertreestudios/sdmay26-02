/// <summary>
/// Class developed to wrap primitive T
/// allowing values to be passed by reference.
/// </summary>
/// <typeparam name="T"></typeparam>
public class Ref<T>
{
    public T Value;

    public Ref(T value)
    {
        Value = value;
    }
}