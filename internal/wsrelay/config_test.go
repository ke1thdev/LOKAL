package wsrelay

import "testing"

func TestRelayEndpointNormalization(t *testing.T) {
	tests := map[string]string{
		"https://class.example.edu":                 "wss://class.example.edu/api/v1/relay/edge",
		"http://127.0.0.1:8081/":                    "ws://127.0.0.1:8081/api/v1/relay/edge",
		"wss://class.example.edu/api/v1/relay/edge": "wss://class.example.edu/api/v1/relay/edge",
		"class.example.edu/lokal":                   "wss://class.example.edu/lokal/api/v1/relay/edge",
	}
	for input, expected := range tests {
		t.Run(input, func(t *testing.T) {
			endpoint, err := (Config{URL: input}).relayEndpoint()
			if err != nil {
				t.Fatal(err)
			}
			if endpoint != expected {
				t.Fatalf("relayEndpoint() = %q, want %q", endpoint, expected)
			}
		})
	}
}

func TestActiveRequiresSecret(t *testing.T) {
	if (Config{URL: "https://class.example.edu"}).Active() {
		t.Fatal("edge relay must not activate without a secret")
	}
	if (Config{HostEnabled: true}).Active() {
		t.Fatal("host relay must not activate without a secret")
	}
	if !(Config{URL: "https://class.example.edu", Secret: "secret"}).Active() {
		t.Fatal("configured edge relay should be active")
	}
}
