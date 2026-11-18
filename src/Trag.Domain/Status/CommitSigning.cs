namespace Trag.Domain.Status;

// How Git signs the commits made in a repository: not at all, or with an OpenPGP key, an SSH key or an X.509 certificate.
public enum CommitSigning { None, OpenPgp, Ssh, X509 }
