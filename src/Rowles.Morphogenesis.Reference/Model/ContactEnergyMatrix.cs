namespace Rowles.Morphogenesis.Reference.Model;

public sealed class ContactEnergyMatrix
{
    private readonly double[,] _values;

    public ContactEnergyMatrix(double[,] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.GetLength(0) == 0 || values.GetLength(0) != values.GetLength(1))
        {
            throw new ArgumentException("The contact-energy matrix must be a non-empty square matrix.", nameof(values));
        }

        _values = (double[,])values.Clone();
        for (int row = 0; row < _values.GetLength(0); row++)
        {
            for (int column = 0; column < _values.GetLength(1); column++)
            {
                double value = _values[row, column];
                if (!double.IsFinite(value))
                {
                    throw new ArgumentException("Contact energies must be finite.", nameof(values));
                }

                if (value != _values[column, row])
                {
                    throw new ArgumentException("The canonical contact-energy matrix must be symmetric.", nameof(values));
                }
            }
        }
    }

    public int TypeCount => _values.GetLength(0);

    public double this[int typeA, int typeB]
    {
        get
        {
            if ((uint)typeA >= (uint)TypeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(typeA));
            }

            if ((uint)typeB >= (uint)TypeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(typeB));
            }

            return _values[typeA, typeB];
        }
    }

    public double[,] ToArray() => (double[,])_values.Clone();
}
