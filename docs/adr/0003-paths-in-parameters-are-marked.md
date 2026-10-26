# Paths in parameters are marked with `$path`

A parameter is free-form JSON, so a rule marks a parameter that is a path: `"to": { "$path": "@customer.Email" }`. Any other JSON is a value, and `$path` must be the object's only key, so the one ambiguous case is a parse error instead of a misread value. Operands keep `{ "path": ... }` and `{ "value": ... }`: an operand is always exactly one kind of node, never free-form JSON, so it needs no marker. The rule is that `$` marks RuleLoom syntax inside free-form JSON. There's no `$value` escape until someone needs to pass a literal `{ "$path": ... }`.

## Considered Options

- **`$path` and `$value` everywhere, operands included.** One spelling, but every stored rule's operands would change, for no gain in operands.
- **`{ "path": ... }` in parameters, like operands.** Rejected: a real value with a `path` key, such as a webhook body `{ "path": "/orders" }`, would be read as a rule path.
- **A prefix inside the string (`"$path.@customer.Email"`).** Rejected: it puts `$path` into the path grammar, where `$` names a state (`$current`, `$previous`); every string would need inspecting, and a text value starting with `$path.` would silently become a path.
