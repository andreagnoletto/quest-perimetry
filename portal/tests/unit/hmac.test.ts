import { createHmac, randomBytes } from "node:crypto";
import { describe, expect, it } from "vitest";
import { verifyHmac } from "@/lib/hmac";

describe("verifyHmac", () => {
  it("matches RFC 4231 test case 6 (key longer than the block)", () => {
    const key = Buffer.alloc(131, 0xaa).toString("base64");
    const data = Buffer.from("Test Using Larger Than Block-Size Key - Hash Key First");
    expect(verifyHmac(data, "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54\n", key)).toBe(true);
  });

  it("accepts the headset sidecar format and rejects a changed byte", () => {
    const key = randomBytes(32);
    const file = Buffer.from('{"ExamId":"x","Locations":[]}');
    const sidecar = createHmac("sha256", key).update(file).digest("hex");
    expect(verifyHmac(file, sidecar.toUpperCase(), key.toString("base64"))).toBe(true);
    const changed = Buffer.from(file); changed[5] ^= 1;
    expect(verifyHmac(changed, sidecar, key.toString("base64"))).toBe(false);
    expect(verifyHmac(file, sidecar, randomBytes(32).toString("base64"))).toBe(false);
    expect(verifyHmac(file, "abc", key.toString("base64"))).toBe(false);
  });
});
