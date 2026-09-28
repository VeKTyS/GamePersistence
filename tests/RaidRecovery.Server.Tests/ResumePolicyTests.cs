using RaidRecovery.Server.Models;
using RaidRecovery.Server.Services;

namespace RaidRecovery.Server.Tests;

public class ResumePolicyTests
{
    private static Snapshot WithHealth(double head, double chest, double headMax = 35, double chestMax = 85)
    {
        return Samples.Snapshot() with
        {
            Player = new PlayerState
            {
                // Invariant: on a French machine, 8.5 would otherwise be written "8,5", which is not JSON
                Profile = Samples.Json(
                    FormattableString.Invariant(
                    $$"""
                    {
                      "Health": {
                        "BodyParts": {
                          "Head": { "Health": { "Current": {{head}}, "Maximum": {{headMax}} } },
                          "Chest": { "Health": { "Current": {{chest}}, "Maximum": {{chestMax}} } },
                          "LeftArm": { "Health": { "Current": 0, "Maximum": 60 } }
                        }
                      }
                    }
                    """
                    )
                ),
            },
        };
    }

    [Fact]
    public void By_default_nothing_is_refused()
    {
        var refusal = ResumePolicy.Refusal(WithHealth(1, 1), resumesDone: 50, new RaidRecoveryConfig());

        Assert.Null(refusal);
    }

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 1, false)]
    [InlineData(2, 3, true)]
    [InlineData(3, 3, false)]
    public void A_raid_is_resumed_as_many_times_as_allowed_and_no_more(int resumesDone, int limit, bool allowed)
    {
        var refusal = ResumePolicy.Refusal(WithHealth(35, 85), resumesDone, new RaidRecoveryConfig { MaxResumesPerRaid = limit });

        Assert.Equal(allowed ? null : ResumePolicy.TooManyResumes, refusal);
    }

    [Fact]
    public void A_raid_cut_with_the_head_almost_gone_is_refused()
    {
        // 3 out of 35: under 10 %
        var refusal = ResumePolicy.Refusal(WithHealth(3, 85), 0, new RaidRecoveryConfig { BlockResumeUnderVitalHealthPercent = 10 });

        Assert.Equal(ResumePolicy.DeathWasImminent, refusal);
    }

    [Fact]
    public void A_raid_cut_with_the_thorax_almost_gone_is_refused()
    {
        var refusal = ResumePolicy.Refusal(WithHealth(35, 8), 0, new RaidRecoveryConfig { BlockResumeUnderVitalHealthPercent = 10 });

        Assert.Equal(ResumePolicy.DeathWasImminent, refusal);
    }

    [Fact]
    public void A_lost_arm_does_not_block_the_recovery()
    {
        var refusal = ResumePolicy.Refusal(WithHealth(35, 85), 0, new RaidRecoveryConfig { BlockResumeUnderVitalHealthPercent = 10 });

        Assert.Null(refusal);
    }

    [Fact]
    public void Health_right_at_the_floor_is_allowed()
    {
        // 8.5 out of 85: exactly 10 %
        var refusal = ResumePolicy.Refusal(WithHealth(35, 8.5), 0, new RaidRecoveryConfig { BlockResumeUnderVitalHealthPercent = 10 });

        Assert.Null(refusal);
    }

    [Fact]
    public void A_snapshot_without_health_is_not_blocked_by_a_rule_it_cannot_be_checked_against()
    {
        var refusal = ResumePolicy.Refusal(Samples.Snapshot(), 0, new RaidRecoveryConfig { BlockResumeUnderVitalHealthPercent = 50 });

        Assert.Null(refusal);
    }

    [Fact]
    public void The_number_of_resumes_is_checked_before_the_health()
    {
        var config = new RaidRecoveryConfig { MaxResumesPerRaid = 1, BlockResumeUnderVitalHealthPercent = 10 };

        var refusal = ResumePolicy.Refusal(WithHealth(1, 1), 1, config);

        Assert.Equal(ResumePolicy.TooManyResumes, refusal);
    }

    [Fact]
    public void The_weakest_vital_part_is_the_one_that_counts()
    {
        Assert.Equal(20, ResumePolicy.LowestVitalPercent(WithHealth(7, 85)));
    }
}
