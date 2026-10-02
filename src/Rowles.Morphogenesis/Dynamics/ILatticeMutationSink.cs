namespace Rowles.Morphogenesis.Dynamics;

public interface ILatticeMutationSink
{
    void AcceptedCopy(int targetIndex, int oldCellId, int newCellId);
}
