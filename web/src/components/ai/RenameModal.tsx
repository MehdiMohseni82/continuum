"use client";

import { useState, useEffect } from "react";
import { Modal } from "../ui/modal";
import Input from "../form/input/InputField";

interface RenameModalProps {
  isOpen: boolean;
  onClose: () => void;
  currentTitle: string;
  onSave: (name: string) => void;
}

export default function RenameModal({
  isOpen,
  onClose,
  currentTitle,
  onSave,
}: RenameModalProps) {
  const [draft, setDraft] = useState(currentTitle);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (isOpen) setDraft(currentTitle);
  }, [isOpen, currentTitle]);

  const handleSave = () => {
    if (draft.trim()) {
      onSave(draft.trim());
    }
    onClose();
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      showCloseButton={false}
      className="w-[calc(100%-32px)]! max-w-[500px] p-6 rounded-2xl z-999999 relative mx-4 sm:mx-auto"
    >
      <h4 className="mb-5 text-base font-semibold text-gray-800 dark:text-white/90">
        Rename Chat
      </h4>
      <Input
        type="text"
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter") handleSave();
        }}
        placeholder="Generate Responsive Login"
        autoFocus
      />
      <div className="mt-6 flex items-center justify-end gap-3">
        <button
          onClick={onClose}
          className="rounded-lg border border-gray-300 bg-white px-5 py-2.5 text-sm font-semibold text-gray-700 hover:bg-gray-50 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700"
        >
          Cancel
        </button>
        <button
          onClick={handleSave}
          className="bg-brand-500 hover:bg-brand-600 rounded-lg px-5 py-2.5 text-sm font-semibold text-white"
        >
          Save
        </button>
      </div>
    </Modal>
  );
}
