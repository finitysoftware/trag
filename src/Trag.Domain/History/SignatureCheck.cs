namespace Trag.Domain.History;

// What Git's check of a commit's signature found, one value for each of its answers (%G?): no signature it could check,
// a good one, good but from a key this computer does not trust, good but expired, made by an expired or a revoked key,
// bad, or one this computer cannot check: the key is missing, Git could not start the program that checks it, or an SSH
// signature has no allowed signers to check against. NotFinished is Trag's own: it stopped waiting for the check.
public enum SignatureCheck { None, Good, Untrusted, Expired, ExpiredKey, Revoked, Bad, CannotCheck, CheckerMissing, NoAllowedSigners, NotFinished }
