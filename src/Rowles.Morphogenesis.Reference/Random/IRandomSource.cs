namespace Rowles.Morphogenesis.Reference.Random;

public interface IRandomSource
{
    int NextInt(int exclusiveUpperBound);

    double NextDouble();
}
