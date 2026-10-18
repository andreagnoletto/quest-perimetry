-- AlterTable
ALTER TABLE "Exam" ALTER COLUMN "importedById" DROP NOT NULL;

-- AlterTable
ALTER TABLE "ExamOrder" ADD COLUMN     "cancelledAt" TIMESTAMP(3),
ADD COLUMN     "deviceId" TEXT,
ADD COLUMN     "sentAt" TIMESTAMP(3);

-- CreateTable
CREATE TABLE "PairingCode" (
    "id" TEXT NOT NULL,
    "codeHash" TEXT NOT NULL,
    "createdById" TEXT NOT NULL,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "expiresAt" TIMESTAMP(3) NOT NULL,
    "attempts" INTEGER NOT NULL DEFAULT 0,
    "usedAt" TIMESTAMP(3),
    "deviceId" TEXT,

    CONSTRAINT "PairingCode_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "DeviceToken" (
    "id" TEXT NOT NULL,
    "deviceId" TEXT NOT NULL,
    "software" TEXT,
    "createdAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "lastUsedAt" TIMESTAMP(3),
    "revokedAt" TIMESTAMP(3),

    CONSTRAINT "DeviceToken_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "LiveSession" (
    "id" TEXT NOT NULL,
    "sessionId" TEXT NOT NULL,
    "deviceId" TEXT,
    "patientRef" TEXT,
    "orderId" TEXT,
    "startedUtc" TIMESTAMP(3) NOT NULL,
    "lastSeenUtc" TIMESTAMP(3) NOT NULL,
    "state" TEXT NOT NULL,
    "finished" BOOLEAN NOT NULL DEFAULT false,
    "outcome" TEXT,
    "seq" INTEGER NOT NULL DEFAULT 0,
    "snapshot" JSONB NOT NULL,

    CONSTRAINT "LiveSession_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "LiveCommand" (
    "id" TEXT NOT NULL,
    "liveSessionId" TEXT NOT NULL,
    "type" TEXT NOT NULL,
    "issuedById" TEXT NOT NULL,
    "issuedAt" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "deliveredAt" TIMESTAMP(3),
    "ackedAt" TIMESTAMP(3),

    CONSTRAINT "LiveCommand_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "PairingCode_codeHash_key" ON "PairingCode"("codeHash");

-- CreateIndex
CREATE INDEX "DeviceToken_deviceId_idx" ON "DeviceToken"("deviceId");

-- CreateIndex
CREATE UNIQUE INDEX "LiveSession_sessionId_key" ON "LiveSession"("sessionId");

-- CreateIndex
CREATE INDEX "LiveSession_lastSeenUtc_idx" ON "LiveSession"("lastSeenUtc");

-- CreateIndex
CREATE INDEX "LiveCommand_liveSessionId_ackedAt_idx" ON "LiveCommand"("liveSessionId", "ackedAt");

-- CreateIndex
CREATE INDEX "ExamOrder_deviceId_fulfilledAt_cancelledAt_idx" ON "ExamOrder"("deviceId", "fulfilledAt", "cancelledAt");

-- AddForeignKey
ALTER TABLE "PairingCode" ADD CONSTRAINT "PairingCode_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "DeviceToken" ADD CONSTRAINT "DeviceToken_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ExamOrder" ADD CONSTRAINT "ExamOrder_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "LiveSession" ADD CONSTRAINT "LiveSession_deviceId_fkey" FOREIGN KEY ("deviceId") REFERENCES "Device"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "LiveCommand" ADD CONSTRAINT "LiveCommand_liveSessionId_fkey" FOREIGN KEY ("liveSessionId") REFERENCES "LiveSession"("id") ON DELETE CASCADE ON UPDATE CASCADE;

