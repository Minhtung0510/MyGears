using System.Text.Json;
namespace MyGears.Core;

public static class RiotLoginPrefill
{
    public static string BuildScript(string? username, string? password) => """
        (() => {
            if (location.protocol !== 'https:' || !['auth.riotgames.com', 'authenticate.riotgames.com'].includes(location.hostname)) return 'blocked';
            const credentials =
        """ + JsonSerializer.Serialize(new { username = username ?? "", password = password ?? "" }) + """
            ;
            const user = document.querySelector('input[data-testid="input-username"][name="username"][type="text"]');
            const pass = document.querySelector('input[data-testid="input-password"][name="password"][type="password"]');
            const fields = [[user, credentials.username], [pass, credentials.password]].filter(([, value]) => value.length > 0);
            if (fields.some(([input]) => !input || input.disabled || input.readOnly || !input.getClientRects().length)) return 'waiting';
            // Never replace a value the user entered while the page was loading.
            if (fields.some(([input, value]) => input.value && input.value !== value)) return 'editing';
            const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
            for (const [input, value] of fields) {
                input.focus();
                // Bypass the framework's per-element value tracker so the input
                // event updates its controlled state, validation and floating label.
                setter.call(input, value);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                input.blur();
            }
            return fields.every(([input, value]) => input.value === value) ? 'filled' : 'waiting';
        })()
        """;
}
