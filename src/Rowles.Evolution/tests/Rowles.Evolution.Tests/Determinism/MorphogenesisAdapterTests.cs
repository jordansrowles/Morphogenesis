using Rowles.Evolution.Evaluation;
using Rowles.Morphogenesis.Experiments.Execution;
using Rowles.Morphogenesis.Experiments.Results;
using Rowles.Morphogenesis.Optimisation;

namespace Rowles.Evolution.Tests.Determinism;

public sealed class MorphogenesisAdapterTests
{
    [Fact]
    public void CandidateMappingDoesNotMutateTemplateManifest()
    {
        var template = MorphogenesisCandidateEvaluator.CreateE02Template();
        string before = template.ToJson();
        MorphogenesisCandidateEvaluator evaluator = new(template);
        NumericCandidate candidate = new(0, [7.5, 12.5, 8], 0);
        var resolved = evaluator.ResolveManifest(candidate);
        Assert.Equal(7.5, resolved.ContactEnergies[1][1]);
        Assert.Equal(7.5, resolved.ContactEnergies[2][2]);
        Assert.Equal(12.5, resolved.ContactEnergies[1][2]);
        Assert.Equal(12.5, resolved.ContactEnergies[2][1]);
        Assert.Equal(8, resolved.FluctuationAmplitude);
        Assert.NotSame(template.ContactEnergies, resolved.ContactEnergies);
        Assert.NotSame(template.ContactEnergies[1], resolved.ContactEnergies[1]);
        MorphogenesisEvaluationResult result = evaluator.Evaluate(candidate);
        Assert.True(result.Evaluation.IsValid, result.Evaluation.FailureReason);
        Assert.Equal(before, template.ToJson());
        Assert.Equal(2, result.ReplicateElapsedMilliseconds.Length);
        Assert.Equal(2, result.Evaluation.Descriptors.Count);

        var independent = ExperimentRunner.Run(resolved);
        double objective = independent.Replicates.Average(replicate =>
            (double)replicate.FinalMeasurements!.TotalCellCellInterfaceCount /
            replicate.Samples.Single(sample => sample.Mcs == 0).Metrics.TotalCellCellInterfaceCount);
        double heterotypic = independent.Replicates.Average(replicate => replicate.FinalMeasurements!.HeterotypicInterfaceFraction);
        double fragmentation = independent.Replicates.Average(replicate =>
        {
            var final = replicate.FinalMeasurements!;
            return (double)final.DomainsByType.Where(domain => domain.TypeId is 1 or 2).Sum(domain => domain.DomainCount) /
                (final.TypeACellCount + final.TypeBCellCount);
        });
        Assert.Equal(objective, result.Evaluation.Objective);
        Assert.Equal(heterotypic, result.Evaluation.Descriptors[0]);
        Assert.Equal(fragmentation, result.Evaluation.Descriptors[1]);
    }

    [Fact]
    public void OutOfBoundsCandidateIsInvalidWithoutRunningSimulation()
    {
        MorphogenesisCandidateEvaluator evaluator = new(MorphogenesisCandidateEvaluator.CreateE02Template());
        MorphogenesisEvaluationResult result = evaluator.Evaluate(new NumericCandidate(4, [21, 5, 2], 0));
        Assert.False(result.Evaluation.IsValid);
        Assert.Equal("candidate-out-of-bounds", result.Evaluation.FailureReason);
        Assert.Empty(result.ReplicateElapsedMilliseconds);
    }

    [Fact]
    public void ZeroInitialInterfaceDenominatorInvalidatesTheWholeCandidate()
    {
        var template = MorphogenesisCandidateEvaluator.CreateE02Template();
        var singleCell = template with
        {
            McsCount = 0,
            Initialiser = template.Initialiser with { CellCount = 1, TypeAProportion = 1 }
        };
        MorphogenesisCandidateEvaluator evaluator = new(singleCell);
        MorphogenesisEvaluationResult result = evaluator.Evaluate(new NumericCandidate(8, [4, 8, 2], 0));
        Assert.False(result.Evaluation.IsValid);
        Assert.Equal("zero-or-missing-initial-interface", result.Evaluation.FailureReason);
        Assert.Equal(2, result.ReplicateElapsedMilliseconds.Length);
    }

    [Fact]
    public void AFailedReplicateInvalidatesTheWholeCandidateAndKeepsItsReason()
    {
        var template = MorphogenesisCandidateEvaluator.CreateE02Template();
        MorphogenesisCandidateEvaluator evaluator = new(template, _ =>
        [
            FailedReplicate(0, "synthetic-runner-failure"),
            FailedReplicate(1, "synthetic-runner-failure")
        ]);
        MorphogenesisEvaluationResult result = evaluator.Evaluate(new NumericCandidate(12, [4, 8, 2], 0));
        Assert.False(result.Evaluation.IsValid);
        Assert.Equal("replicate-failed: synthetic-runner-failure", result.Evaluation.FailureReason);
        Assert.Equal(2, result.ReplicateElapsedMilliseconds.Length);
    }

    [Fact]
    public void CommonRandomNumbersMakeResultsIndependentOfCandidateEvaluationOrder()
    {
        MorphogenesisCandidateEvaluator evaluator = new(MorphogenesisCandidateEvaluator.CreateE02Template());
        NumericCandidate first = new(10, [4, 8, 2], 0);
        NumericCandidate second = new(11, [16, 22, 18], 1);
        MorphogenesisEvaluationResult firstForward = evaluator.Evaluate(first);
        MorphogenesisEvaluationResult secondForward = evaluator.Evaluate(second);
        MorphogenesisEvaluationResult secondReverse = evaluator.Evaluate(second);
        MorphogenesisEvaluationResult firstReverse = evaluator.Evaluate(first);
        Assert.True(firstForward.Evaluation.IsValid, firstForward.Evaluation.FailureReason);
        Assert.True(secondForward.Evaluation.IsValid, secondForward.Evaluation.FailureReason);
        Assert.Equal(firstForward.Evaluation.Objective, firstReverse.Evaluation.Objective);
        Assert.Equal(firstForward.Evaluation.Descriptors, firstReverse.Evaluation.Descriptors);
        Assert.Equal(secondForward.Evaluation.Objective, secondReverse.Evaluation.Objective);
        Assert.Equal(secondForward.Evaluation.Descriptors, secondReverse.Evaluation.Descriptors);
    }

    private static ExperimentReplicateResult FailedReplicate(int index, string reason) => new(
        "m25-e02", $"m25-e02-r{index + 1:D4}", index, 0, 0, 0, ReplicateRunStatus.Failed,
        reason, [], null, [], 0, 0, 0, 0, 0, 1, 0, 0);
}
