namespace Harborline.Blocks.Scheduling.Planning;

public sealed class FiniteCandidateCompiler
{
    public CompiledPlanningProblem Compile(SchedulingProfile profile)
    {
        Validate(profile);

        var candidates = new Dictionary<string, IReadOnlyList<AssignmentCandidate>>(StringComparer.Ordinal);
        foreach (var activity in profile.Activities.OrderBy(value => value.Id, StringComparer.Ordinal))
        {
            var window = profile.TimeWindows.Single(value => value.ActivityId == activity.Id);
            var requirements = profile.ResourceRequirements
                .Where(value => value.ActivityId == activity.Id)
                .OrderBy(value => value.Id, StringComparer.Ordinal)
                .ToArray();
            var activityCandidates = new List<AssignmentCandidate>();

            for (var start = window.EarliestStartSlot; start <= window.LatestStartSlot; start++)
            {
                var end = checked(start + activity.DurationSlots);
                var resourceSelections = requirements
                    .Select(requirement => SelectResources(profile.Resources, requirement, start, end))
                    .ToArray();

                foreach (var selection in CartesianProduct(resourceSelections))
                {
                    var flattened = selection.SelectMany(value => value).ToArray();
                    if (flattened.Distinct(StringComparer.Ordinal).Count() != flattened.Length)
                    {
                        continue;
                    }

                    activityCandidates.Add(new AssignmentCandidate(
                        activity.Id,
                        start,
                        end,
                        flattened.Order(StringComparer.Ordinal).ToArray()));
                }
            }

            candidates.Add(
                activity.Id,
                activityCandidates
                    .OrderBy(value => value.StartSlot)
                    .ThenBy(value => string.Join('|', value.ResourceIds), StringComparer.Ordinal)
                    .ToArray());
        }

        return new CompiledPlanningProblem(
            profile.ProfileId,
            profile.InputVersion,
            profile.Activities,
            candidates,
            profile.Precedence);
    }

    private static IReadOnlyList<IReadOnlyList<string>> SelectResources(
        IReadOnlyList<PlanningResource> resources,
        ResourceRequirement requirement,
        int start,
        int end)
    {
        var eligible = resources
            .Where(resource => resource.Capabilities.Contains(requirement.Capability))
            .Where(resource => Enumerable.Range(start, end - start).All(resource.AvailableSlots.Contains))
            .OrderBy(resource => resource.Id, StringComparer.Ordinal)
            .Select(resource => resource.Id)
            .ToArray();

        return Choose(eligible, requirement.Quantity).ToArray();
    }

    private static IEnumerable<IReadOnlyList<string>> Choose(
        IReadOnlyList<string> values,
        int count,
        int start = 0,
        IReadOnlyList<string>? prefix = null)
    {
        prefix ??= [];
        if (count == 0)
        {
            yield return prefix;
            yield break;
        }

        for (var index = start; index <= values.Count - count; index++)
        {
            foreach (var selection in Choose(values, count - 1, index + 1, [.. prefix, values[index]]))
            {
                yield return selection;
            }
        }
    }

    private static IEnumerable<IReadOnlyList<IReadOnlyList<string>>> CartesianProduct(
        IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> selections,
        int index = 0,
        IReadOnlyList<IReadOnlyList<string>>? prefix = null)
    {
        prefix ??= [];
        if (index == selections.Count)
        {
            yield return prefix;
            yield break;
        }

        foreach (var selection in selections[index])
        {
            foreach (var result in CartesianProduct(selections, index + 1, [.. prefix, selection]))
            {
                yield return result;
            }
        }
    }

    private static void Validate(SchedulingProfile profile)
    {
        var activityIds = profile.Activities.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (activityIds.Count != profile.Activities.Count || profile.Activities.Any(value => value.DurationSlots <= 0))
        {
            throw new UnsupportedProfileException("Activities must have unique ids and positive durations.");
        }

        var resourceIds = profile.Resources.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (resourceIds.Count != profile.Resources.Count ||
            profile.Resources.Any(value => string.IsNullOrWhiteSpace(value.Id) || value.Capabilities.Count == 0))
        {
            throw new UnsupportedProfileException("Resources must have unique ids and capabilities.");
        }

        var windowIds = profile.TimeWindows.Select(value => value.ActivityId).ToHashSet(StringComparer.Ordinal);
        if (windowIds.Count != profile.TimeWindows.Count ||
            profile.TimeWindows.Any(value =>
                !activityIds.Contains(value.ActivityId) ||
                value.EarliestStartSlot > value.LatestStartSlot))
        {
            throw new UnsupportedProfileException("Time windows must be unique, ordered, and reference activities.");
        }

        var requirementIds = profile.ResourceRequirements
            .Select(value => value.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (requirementIds.Count != profile.ResourceRequirements.Count)
        {
            throw new UnsupportedProfileException("Resource requirements must have unique ids.");
        }

        var precedenceIds = profile.Precedence.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        if (precedenceIds.Count != profile.Precedence.Count)
        {
            throw new UnsupportedProfileException("Precedence constraints must have unique ids.");
        }

        foreach (var activityId in activityIds)
        {
            if (profile.TimeWindows.Count(value => value.ActivityId == activityId) != 1)
            {
                throw new UnsupportedProfileException($"Activity {activityId} must have exactly one time window.");
            }

            if (!profile.ResourceRequirements.Any(value => value.ActivityId == activityId))
            {
                throw new UnsupportedProfileException($"Activity {activityId} must have a resource requirement.");
            }
        }

        if (profile.ResourceRequirements.Any(value =>
                !activityIds.Contains(value.ActivityId) ||
                string.IsNullOrWhiteSpace(value.Capability) ||
                value.Quantity <= 0))
        {
            throw new UnsupportedProfileException("Resource requirements must reference activities and positive quantities.");
        }

        if (profile.Precedence.Any(value =>
                !activityIds.Contains(value.BeforeActivityId) ||
                !activityIds.Contains(value.AfterActivityId) ||
                value.BeforeActivityId == value.AfterActivityId ||
                value.MinimumGapSlots < 0))
        {
            throw new UnsupportedProfileException("Precedence constraints must reference distinct activities.");
        }
    }
}
