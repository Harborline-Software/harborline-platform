# HLP-0001: Deep modules and projections

Status: accepted

Each canonical Platform module owns exactly one caller-facing interface. Target-specific code is an
implementation, adapter, generated binding, host, or compatibility facade recorded as a projection;
only code at a real variation seam is called an adapter. Conformance enters through public artifact
or application interfaces and uses neutral fixtures. Sibling projections do not generate from or
import one another.

Repository-local build orchestration is allowed. Cross-repository source links and a checkout-root
workspace are not. Releases consume same-repository source or packed, versioned dependencies.
