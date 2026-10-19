using System.Collections.Generic;
using GilDelta.Events;
using GilDelta.Events.Rules;
using GilDelta.Wallet;

namespace GilDelta.Tests.Events.Rules;

public class MailRuleTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly WalletId Self = new(WalletKind.Self, "");

    private static GameContext Ctx(params string[] addons) =>
        new(new HashSet<string>(addons), Array.Empty<WalletDiff>(), T0);

    [Fact]
    public void Self_increase_with_LetterViewer_open_is_a_mail_receipt()
    {
        var ok = new MailRule().TryClassify(
            new WalletDiff(Self, 0, 1_000_000, T0), Ctx("LetterList", "LetterViewer"), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.MailReceive, ev!.Category);
        Assert.Equal(1_000_000, ev.Amount);
        Assert.Contains("rule=MailRule", ev.Note);
    }

    [Fact]
    public void Self_decrease_with_LetterEditor_open_is_a_mail_send()
    {
        var ok = new MailRule().TryClassify(
            new WalletDiff(Self, 1_000_000, 0, T0), Ctx("LetterList", "LetterEditor"), out var ev);

        Assert.True(ok);
        Assert.Equal(GilEventCategory.MailSend, ev!.Category);
        Assert.Equal(-1_000_000, ev.Amount);
    }

    [Fact]
    public void The_two_directions_do_not_cross()
    {
        var rule = new MailRule();
        Assert.False(rule.TryClassify(
            new WalletDiff(Self, 0, 1_000, T0), Ctx("LetterEditor"), out _));
        Assert.False(rule.TryClassify(
            new WalletDiff(Self, 1_000, 0, T0), Ctx("LetterViewer"), out _));
    }

    [Fact]
    public void LetterList_alone_does_not_match()
    {
        Assert.False(new MailRule().TryClassify(
            new WalletDiff(Self, 0, 1_000, T0), Ctx("LetterList"), out _));
    }

    [Fact]
    public void A_retainer_wallet_never_matches()
    {
        Assert.False(new MailRule().TryClassify(
            new WalletDiff(new WalletId(WalletKind.Retainer, "Yui"), 0, 1_000, T0),
            Ctx("LetterViewer"), out _));
    }

    [Fact]
    public void A_zero_change_does_not_match()
    {
        Assert.False(new MailRule().TryClassify(
            new WalletDiff(Self, 1_000, 1_000, T0), Ctx("LetterViewer", "LetterEditor"), out _));
    }
}
