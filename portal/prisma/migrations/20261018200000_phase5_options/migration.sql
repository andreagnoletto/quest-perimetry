-- AlterTable
ALTER TABLE "Exam" ADD COLUMN     "fovealHfaDb" DOUBLE PRECISION,
ADD COLUMN     "gazeTracked" BOOLEAN NOT NULL DEFAULT false;

-- AlterTable
ALTER TABLE "ExamOrder" ADD COLUMN     "fixationMode" TEXT,
ADD COLUMN     "fovealThreshold" BOOLEAN,
ADD COLUMN     "gridId" TEXT,
ADD COLUMN     "responseMode" TEXT;

