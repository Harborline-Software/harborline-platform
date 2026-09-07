# HLP-0002: UI seams and release groups

Status: accepted

UI behavior is organized by module interface, with framework and styling variation behind named
seams. React and Blazor are projections of the same semantics, not authorities for one another.
Accessibility, form behavior, compatibility identities, and package size are interface/release
concerns. npm and NuGet artifacts remain independently installable and are tested from packed files
with sibling source unavailable.
