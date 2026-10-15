import { expect, it as test } from "vitest";

import { fakeKunaCalls } from "./fakeKuna.js";
import { exportsOf, run, session } from "./kunaSessionFixture.js";

const it = test.skipIf(process.platform === "win32");

const inventoriesOf = async (kuna: string) =>
  (await fakeKunaCalls(kuna)).filter(({ argv }) => argv[0] === "functions");

it("lists a cold target from the export's inventory without a second run", async () => {
  const { kuna, open } = await session("rea-kuna-export-inventory-", {
    project: "hang",
    inventoryDelayMs: 200,
  });
  const client = open();
  try {
    expect(
      (await run(client, "search_procedures", { pattern: "parse" })).result,
    ).toEqual([{ address: "0x1040", value: "parse" }]);
    expect(
      (await run(client, "procedure_address", { procedure: "main" })).result,
    ).toBe("0x1000");
    // The export goes on to decompile; its inventory answered both requests.
    await expect.poll(() => exportsOf(kuna)).toHaveLength(1);
    expect(await inventoriesOf(kuna)).toHaveLength(1);
  } finally {
    expect(await client.close()).toEqual({ ok: true, value: null });
  }
});

it("gives a session that joins a running export the inventory it already listed", async () => {
  const { kuna, open } = await session("rea-kuna-joined-inventory-", {
    project: "hang",
  });
  const first = open();
  const second = open();
  try {
    await run(first, "list_procedures");
    await expect.poll(() => exportsOf(kuna)).toHaveLength(1);
    expect(
      (await run(second, "procedure_address", { procedure: "parse" })).result,
    ).toBe("0x1040");
    expect(await inventoriesOf(kuna)).toHaveLength(1);
  } finally {
    expect(await second.close()).toEqual({ ok: true, value: null });
    expect(await first.close()).toEqual({ ok: true, value: null });
  }
});

it("runs its own inventory when the export ends before listing", async () => {
  const { kuna, open } = await session("rea-kuna-inventory-fallback-", {
    inventory: "not-json",
  });
  const client = open();
  try {
    expect(await client.execute("list_procedures", {})).toMatchObject({
      ok: false,
      error: {
        userMessage: expect.stringContaining(
          "Kuna's function inventory is not usable",
        ),
      },
    });
    // The export's listing failed first; the session's own run reports why.
    expect(await inventoriesOf(kuna)).toHaveLength(2);
  } finally {
    expect(await client.close()).toEqual({ ok: true, value: null });
  }
});
