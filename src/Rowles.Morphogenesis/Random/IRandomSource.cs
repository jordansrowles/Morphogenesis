namespace Rowles.Morphogenesis.Random;

public interface IRandomSource
{
    int NextInt(int exclusiveUpperBound);

    double NextDouble();
}
